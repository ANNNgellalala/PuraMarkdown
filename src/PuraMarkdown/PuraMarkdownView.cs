using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig.Syntax;
using PuraMarkdown.Rendering;
using PuraMarkdown.Security;
using PuraMarkdown.Syntax;

namespace PuraMarkdown;

/// <summary>
/// Avalonia 只读 Markdown 控件。消费 Markdown 文本，输出 Avalonia 视觉树；
/// 交互组件通过 <see cref="InteractionOccurred"/> 上报，不管理对话。
/// </summary>
public sealed class PuraMarkdownView : ContentControl, IDisposable
{
    /// <summary><see cref="Markdown"/> 的样式属性。</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<PuraMarkdownView, string?>(nameof(Markdown));

    /// <summary><see cref="Options"/> 的样式属性。</summary>
    public static readonly StyledProperty<PuraMarkdownOptions?> OptionsProperty =
        AvaloniaProperty.Register<PuraMarkdownView, PuraMarkdownOptions?>(nameof(Options));

    private readonly Grid _root = new() { Tag = "pura-root" };
    private readonly StackPanel _host = new();
    private readonly TextBlock _liveRegion;
    private readonly List<BlockRecord> _records = [];
    private readonly ScrollViewer _virtualScroll = new();
    private readonly ItemsRepeater _virtualRepeater = new();
    private readonly ObservableCollection<VirtualBlock> _virtualBlocks = [];
    private readonly HashSet<string> _warnedKeys = [];
    private readonly Dictionary<string, PuraInteractionEventArgs> _componentStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);
    private bool _virtualized;
    private bool _lastRenderWasStreaming;
    private int _tailRecordCount;
    private int _minStreamingVersion;
    private int _streamingSessionId;
    private PuraTheme? _appliedTheme;
    private PuraStreamingSession? _activeSession;

    /// <summary>创建 Markdown 只读视图。</summary>
    public PuraMarkdownView()
    {
        _virtualRepeater.Layout = new Avalonia.Layout.StackLayout();
        _virtualRepeater.ItemsSource = _virtualBlocks;
        _virtualRepeater.ItemTemplate = new FuncDataTemplate<VirtualBlock>(
            (block, _) => RenderVirtualBlock(block));
        _virtualScroll.Content = _virtualRepeater;

        _liveRegion = new TextBlock
        {
            Text = string.Empty,
            Opacity = 0,
            IsHitTestVisible = false,
            Focusable = false,
            TextWrapping = TextWrapping.NoWrap,
            Tag = "pura-live-region",
        };
        AutomationProperties.SetLiveSetting(_liveRegion, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(_liveRegion, "Markdown 状态");

        _root.Children.Add(_host);
        _root.Children.Add(_liveRegion);
        Content = _root;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    /// <summary>全量 Markdown 文本；变更时触发整篇重渲染。</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>控件配置（管道、主题、资源加载器、安全策略等）。</summary>
    public PuraMarkdownOptions? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>交互事件（选择/确认）。库只上报，宿主决定后续行为。</summary>
    public event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;

    /// <summary>非致命问题：降级、插件异常、资源拒绝、预算超限。</summary>
    public event EventHandler<PuraWarningEventArgs>? Warning;

    /// <summary>无障碍播报（流式完成/中断）；宿主可接屏幕阅读器。</summary>
    public event EventHandler<string>? Announced;

    /// <summary>链接被点击；设置 <see cref="PuraLinkClickedEventArgs.Handled"/> 可阻止默认打开。</summary>
    public event EventHandler<PuraLinkClickedEventArgs>? LinkClicked;

    /// <summary>图片开始加载。</summary>
    public event EventHandler<PuraImageEventArgs>? ImageLoading;

    /// <summary>图片加载完成（成功/拒绝/失败）。</summary>
    public event EventHandler<PuraImageLoadedEventArgs>? ImageLoaded;

    /// <summary>已渲染块记录（流式嫁接锚点；只读）。</summary>
    public IReadOnlyList<BlockRecord> Blocks => _records;

    /// <summary>已上报的交互状态（按 ComponentId），可用于会话持久化。</summary>
    public IReadOnlyDictionary<string, PuraInteractionEventArgs> InteractionStates => _componentStates;

    /// <summary>当前活动流式会话（无则为 null）。</summary>
    public PuraStreamingSession? ActiveSession => _activeSession;

    /// <summary>是否正在流式渲染。</summary>
    public bool IsStreaming => _activeSession?.State == PuraStreamingState.Streaming;

    /// <summary>开始一次流式会话（同一控件同时只允许一个进行中的会话）。</summary>
    /// <param name="options">流式选项；默认后台解析 + UI 合帧。</param>
    /// <returns>流式会话。</returns>
    public PuraStreamingSession BeginStreaming(PuraStreamingOptions? options = null)
    {
        if (_activeSession is not null && _activeSession.State == PuraStreamingState.Streaming)
        {
            throw new InvalidOperationException("已有进行中的流式会话");
        }

        _activeSession = new PuraStreamingSession(this, options ?? new PuraStreamingOptions());
        return _activeSession;
    }

    /// <summary>写入单条交互状态（下次渲染/重渲染时恢复）。</summary>
    /// <param name="state">交互状态。</param>
    public void SetInteractionState(PuraInteractionEventArgs state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrEmpty(state.ComponentId))
        {
            throw new ArgumentException("交互状态必须带 ComponentId", nameof(state));
        }

        _componentStates[state.ComponentId] = state;
    }

    /// <summary>批量恢复交互状态（历史回放/持久化恢复）；已有内容会重渲染。</summary>
    /// <param name="states">交互状态集合。</param>
    public void ApplyInteractionStates(IEnumerable<PuraInteractionEventArgs> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (var state in states)
        {
            if (!string.IsNullOrEmpty(state.ComponentId))
            {
                _componentStates[state.ComponentId] = state;
            }
        }

        if (!string.IsNullOrWhiteSpace(GetCurrentText()))
        {
            Render();
        }
    }

    /// <summary>清空交互状态（新会话/重置时使用）。</summary>
    public void ClearInteractionStates()
    {
        if (_componentStates.Count == 0)
        {
            return;
        }

        _componentStates.Clear();
        if (!string.IsNullOrWhiteSpace(GetCurrentText()))
        {
            Render();
        }
    }

    /// <summary>全量渲染当前 Markdown。</summary>
    public void Render() => RenderText(Markdown ?? string.Empty, isStreaming: false);

    /// <summary>中断当前流式会话（保留已渲染内容）。</summary>
    public void AbortStreaming()
    {
        if (_activeSession is { State: PuraStreamingState.Streaming } session)
        {
            _ = session.AbortAsync();
        }
    }

    /// <summary>释放视图：取消活动流式会话并释放资源。</summary>
    public void Dispose()
    {
        if (_activeSession is not null)
        {
            _ = _activeSession.DisposeAsync();
            _activeSession = null;
        }
    }

    /// <summary>触发一次无障碍播报（宿主可转发给平台播报器）。</summary>
    public void Announce(string message)
    {
        _liveRegion.Text = message;
        Announced?.Invoke(this, message);
    }

    /// <summary>
    /// 预热渲染路径：用一个代表性 Markdown 在离屏探针控件中走完整渲染（高亮/图表/组件/布局），
    /// 把首次初始化（JIT、正则编译、字体与控件样式）从"首条流式消息"提前到应用启动阶段。
    /// 必须在 UI 线程调用。
    /// </summary>
    /// <param name="markdown">预热用 Markdown（建议覆盖代码块、表格、图表与组件）。</param>
    public void Prewarm(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var options = Options;
        if (options is null)
        {
            return;
        }

        var probe = new PuraMarkdownView { Options = options };
        probe.Markdown = markdown;
        probe.Measure(new Size(1024, 2048));
        probe.Arrange(new Rect(probe.DesiredSize));
    }

    /// <summary>获取当前文档的标题大纲（含自动生成的锚点）。</summary>
    public IReadOnlyList<PuraHeadingInfo> GetOutline()
    {
        var text = GetCurrentText();
        var options = Options;
        if (string.IsNullOrEmpty(text) || options is null)
        {
            return [];
        }

        try
        {
            return options.Pipeline.ParseDocument(text)
                .Outline.Headings
                .Select(heading => new PuraHeadingInfo(heading.Level, heading.Text, heading.Anchor))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>获取当前内容的纯文本（不含 Markdown 标记），可用于复制/搜索/无障碍。</summary>
    public string GetPlainText()
    {
        var text = GetCurrentText();
        var options = Options;
        if (string.IsNullOrEmpty(text) || options is null)
        {
            return string.Empty;
        }

        try
        {
            return MarkdownText.GetPlainText(options.Pipeline.Parse(text));
        }
        catch
        {
            return text;
        }
    }

    /// <summary>复制当前内容纯文本到剪贴板。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task CopyAllAsync(CancellationToken cancellationToken = default)
    {
        var options = Options;
        if (options is null)
        {
            return;
        }

        await options.Clipboard.SetTextAsync(GetPlainText(), cancellationToken).ConfigureAwait(true);
    }

    /// <summary>滚动到指定标题锚点（虚拟化模式使用估算偏移）。</summary>
    /// <param name="anchor">锚点文本，可带前导 #。</param>
    /// <returns>是否找到并滚动。</returns>
    public bool ScrollToAnchor(string anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return false;
        }

        var target = anchor.TrimStart('#');
        if (_anchors.TryGetValue(target, out var anchored))
        {
            anchored.BringIntoView();
            return true;
        }

        for (var i = 0; i < _records.Count; i++)
        {
            if (_records[i].Node is not HeadingBlock heading)
            {
                continue;
            }

            if (!string.Equals(Slug(MarkdownText.GetHeadingText(heading)), target, StringComparison.Ordinal))
            {
                continue;
            }

            if (_virtualized)
            {
                var extent = _virtualScroll.Extent.Height;
                if (extent > 0)
                {
                    _virtualScroll.Offset = new Vector(0, extent * i / Math.Max(1, _records.Count));
                    return true;
                }

                return false;
            }

            if (_records[i].View is { } control)
            {
                control.BringIntoView();
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>按当前主题重新渲染（系统主题切换后可调用）。</summary>
    public void RefreshTheme() => Render();

    private string GetCurrentText() => _activeSession?.CurrentText ?? Markdown ?? string.Empty;

    private static string Slug(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var pendingDash = false;
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_')
            {
                pendingDash = true;
            }
        }

        return builder.Length == 0 ? "section" : builder.ToString();
    }
    internal int BeginStreamingRender()
    {
        if (_virtualized)
        {
            _virtualBlocks.Clear();
        }
        else
        {
            _host.Children.Clear();
        }

        _records.Clear();
        _tailRecordCount = 0;
        _minStreamingVersion = 0;
        _lastRenderWasStreaming = true;
        return ++_streamingSessionId;
    }

    internal void MarkStreamingClosed(int minVersion)
    {
        _minStreamingVersion = minVersion;
        _lastRenderWasStreaming = false;
    }

    /// <summary>
    /// 应用一次流式刷新：稳定窗口追加控件，尾部窗口按内容哈希复用/增量更新。
    /// 必须在 UI 线程调用。
    /// </summary>
    internal void ApplyStreamingUpdate(StreamingUpdate update)
    {
        if (update.SessionId != _streamingSessionId || update.Version < _minStreamingVersion)
        {
            return;
        }

        var options = Options;
        if (options is null)
        {
            return;
        }

        EnsureVirtualized(options.EnableBlockVirtualization);

        // 稳定窗口会取代上一次的尾部块：必须先移除旧尾部，再追加稳定块，
        // 否则旧尾部控件会残留并与新提交的块重复显示。
        if (update.CommittedWindows.Count > 0 && _tailRecordCount > 0)
        {
            RemoveTailRecords();
        }

        foreach (var window in update.CommittedWindows)
        {
            ApplyCommittedWindow(window, options);
        }

        if (update.TailWindow is not null)
        {
            ApplyTailWindow(update.TailWindow, options);
        }
        else
        {
            RemoveTailRecords();
        }

        _lastRenderWasStreaming = true;
    }

    internal void ClearStreamingTail() => RemoveTailRecords();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty || change.Property == OptionsProperty)
        {
            Render();
        }
    }

    /// <summary>
    /// 渲染后台准备好的完整文档：高亮已在后台计算，按帧预算分批挂载控件，避免长消息 Complete 时一次性冻结 UI。
    /// 必须在 UI 线程调用。
    /// </summary>
    internal async Task RenderPreparedAsync(PreparedDocument prepared)
    {
        var options = Options;
        if (options is null)
        {
            return;
        }

        EnsureVirtualized(options.EnableBlockVirtualization);
        if (_virtualized)
        {
            _virtualBlocks.Clear();
        }
        else
        {
            _host.Children.Clear();
        }

        _records.Clear();
        _tailRecordCount = 0;

        foreach (var warning in prepared.Warnings)
        {
            Warn(warning.Code, warning.Message, warning.NodeType, warning.Renderer);
        }

        var context = CreateContext(options, prepared.Sanitized, isStreaming: false, isTail: false, prepared.Highlights);

        if (_virtualized)
        {
            RenderVirtualDocument(prepared.Document, prepared.Text, context, isStreaming: false);
            return;
        }

        var frameBudget = Stopwatch.StartNew();
        var frameBlocks = 0;

        foreach (var block in prepared.Document)
        {
            if (prepared.Sanitized.IsBlocked(block))
            {
                continue;
            }

            Control view;
            try
            {
                view = context.RenderMarkdown(block);
            }
            catch (Exception ex)
            {
                Warn("PURA-RENDER-900", $"块渲染异常，降级为文本：{ex.Message}", block.GetType().Name);
                view = new TextBlock
                {
                    Text = $"[渲染失败: {block.GetType().Name}] {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(options.Theme.Colors.Danger),
                    Margin = new Thickness(0, 0, 0, 8),
                };
            }

            _records.Add(new BlockRecord(
                _records.Count,
                block.Span.Start,
                block.Span.End,
                HashOf(prepared.Text, block),
                block,
                view));
            _host.Children.Add(view);

            frameBlocks++;
            if (frameBlocks >= 16 && frameBudget.ElapsedMilliseconds >= 8)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                frameBlocks = 0;
                frameBudget.Restart();
            }
        }

        _lastRenderWasStreaming = false;
    }

    internal void RenderText(string text, bool isStreaming)
    {
        var options = Options;
        if (options is null)
        {
            return;
        }

        PuraDocument document;
        try
        {
            document = options.Pipeline.ParseDocument(text, 0, options.CreateSyntaxOptions());
        }
        catch (Exception ex)
        {
            PrepareRender();
            Warn("PURA-PARSE-001", $"Markdig 解析异常，降级为纯文本：{ex.Message}");
            AddFallbackText(text);
            return;
        }

        RenderDocumentCore(document, isStreaming);
    }

    /// <summary>渲染已解析的语法文档（不重新解析 Markdown）。</summary>
    /// <param name="document">语法层文档。</param>
    public void RenderDocument(PuraDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        RenderDocumentCore(document, isStreaming: false);
    }

    private void RenderDocumentCore(PuraDocument document, bool isStreaming)
    {
        var options = Options;
        if (options is null)
        {
            return;
        }

        PrepareRender();

        var text = document.Text;
        var limits = options.Limits;
        if (text.Length > limits.MaxDocumentChars)
        {
            Warn("PURA-LIM-001", $"文档超过 {limits.MaxDocumentChars} 字符，已截断");
            text = text[..limits.MaxDocumentChars];
        }

        var markdown = document.Markdown;
        var sanitized = document.Sanitized
            ?? options.Pipeline.Syntax.Options.Sanitizer.Sanitize(markdown, options.CreateSyntaxOptions());
        foreach (var sanitizerWarning in sanitized.Warnings)
        {
            WarnSanitizer(sanitizerWarning);
        }

        var context = CreateContext(options, sanitized, isStreaming, isTail: false);

        if (_virtualized)
        {
            RenderVirtualDocument(markdown, text, context, isStreaming);
            return;
        }

        foreach (var block in markdown)
        {
            if (sanitized.IsBlocked(block))
            {
                continue;
            }

            try
            {
                var view = context.RenderMarkdown(block);
                _records.Add(new BlockRecord(
                    _records.Count,
                    block.Span.Start,
                    block.Span.End,
                    HashOf(text, block),
                    block,
                    view));
                _host.Children.Add(view);
            }
            catch (Exception ex)
            {
                Warn("PURA-RENDER-900", $"块渲染异常，降级为文本：{ex.Message}", block.GetType().Name);
                var fallback = new TextBlock
                {
                    Text = $"[渲染失败: {block.GetType().Name}] {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(options.Theme.Colors.Danger),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                _records.Add(new BlockRecord(
                    _records.Count,
                    block.Span.Start,
                    block.Span.End,
                    HashOf(text, block),
                    block,
                    fallback));
                _host.Children.Add(fallback);
            }
        }

        _lastRenderWasStreaming = isStreaming;
    }

    private void PrepareRender()
    {
        _warnedKeys.Clear();
        EnsureVirtualized(Options?.EnableBlockVirtualization ?? false);
        if (_virtualized)
        {
            _virtualBlocks.Clear();
        }
        else
        {
            _host.Children.Clear();
        }

        _records.Clear();
        _tailRecordCount = 0;
        _minStreamingVersion = 0;
    }

    private void ApplyCommittedWindow(StreamingWindowUpdate window, PuraMarkdownOptions options)
    {
        var context = CreateContext(options, window.Sanitized, isStreaming: true, isTail: false, window.Highlights);
        foreach (var sanitizerWarning in window.Sanitized.Warnings)
        {
            WarnSanitizer(sanitizerWarning);
        }

        if (_virtualized)
        {
            foreach (var block in window.Blocks)
            {
                var hash = HashOf(window.SourceText, block);
                _virtualBlocks.Add(new VirtualBlock(block, hash, window.Offset + block.Span.Start, window.Offset + block.Span.End, context));
                _records.Add(new BlockRecord(
                    _records.Count,
                    window.Offset + block.Span.Start,
                    window.Offset + block.Span.End,
                    hash,
                    block,
                    null));
            }

            return;
        }

        foreach (var block in window.Blocks)
        {
            if (window.Sanitized.IsBlocked(block))
            {
                continue;
            }

            Control view;
            try
            {
                view = context.RenderMarkdown(block);
            }
            catch (Exception ex)
            {
                Warn("PURA-RENDER-900", $"块渲染异常，降级为文本：{ex.Message}", block.GetType().Name);
                view = new TextBlock
                {
                    Text = $"[渲染失败: {block.GetType().Name}] {ex.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(options.Theme.Colors.Danger),
                    Margin = new Thickness(0, 0, 0, 8),
                };
            }

            _records.Add(new BlockRecord(
                _records.Count,
                window.Offset + block.Span.Start,
                window.Offset + block.Span.End,
                HashOf(window.SourceText, block),
                block,
                view));
            _host.Children.Add(view);
        }
    }

    private void ApplyTailWindow(StreamingWindowUpdate window, PuraMarkdownOptions options)
    {
        var context = CreateContext(options, window.Sanitized, isStreaming: true, isTail: true, window.Highlights);

        if (_virtualized)
        {
            ApplyVirtualTail(window, context);
            return;
        }

        var tailStart = _records.Count - _tailRecordCount;
        var oldTail = _tailRecordCount == 0
            ? []
            : _records.GetRange(tailStart, _tailRecordCount);
        if (_tailRecordCount > 0)
        {
            _records.RemoveRange(tailStart, _tailRecordCount);
        }

        // 内容未变的前缀块复用已有控件；只替换/更新变化的后缀。
        var reuse = 0;
        while (reuse < oldTail.Count && reuse < window.Blocks.Count)
        {
            var old = oldTail[reuse];
            var block = window.Blocks[reuse];
            if (old.Hash == HashOf(window.SourceText, block) && old.Node.GetType() == block.GetType())
            {
                reuse++;
            }
            else
            {
                break;
            }
        }

        for (var i = reuse; i < oldTail.Count; i++)
        {
            if (oldTail[i].View is { } oldView)
            {
                _host.Children.Remove(oldView);
            }
        }

        var baseIndex = _records.Count;
        var newTail = new List<BlockRecord>(window.Blocks.Count);
        for (var i = 0; i < window.Blocks.Count; i++)
        {
            var block = window.Blocks[i];
            var hash = HashOf(window.SourceText, block);
            var start = window.Offset + block.Span.Start;
            var end = window.Offset + block.Span.End;

            if (i < reuse)
            {
                newTail.Add(oldTail[i] with
                {
                    Index = baseIndex + i,
                    Start = start,
                    End = end,
                    Hash = hash,
                    Node = block,
                });
                continue;
            }

            Control view;
            if (i < oldTail.Count
                && oldTail[i].View is { } existingView
                && oldTail[i].Node.GetType() == block.GetType()
                && TryIncrementalUpdate(existingView, oldTail[i].Node, block, context))
            {
                view = existingView;
                // 后缀旧控件已被移出宿主，更新后必须重新挂回。
                _host.Children.Add(view);
            }
            else
            {
                view = context.RenderMarkdown(block);
                _host.Children.Add(view);
            }

            newTail.Add(new BlockRecord(baseIndex + i, start, end, hash, block, view));
        }

        _records.AddRange(newTail);
        _tailRecordCount = window.Blocks.Count;
    }

    private bool TryIncrementalUpdate(
        Control existing,
        MarkdownObject oldNode,
        MarkdownObject newNode,
        RenderContext context)
    {
        try
        {
            foreach (var renderer in context.Renderers.ResolveCandidates(newNode, context))
            {
                if (renderer is IIncrementalPuraRenderer incremental
                    && incremental.CanUpdate(oldNode, newNode, context))
                {
                    incremental.Update(existing, newNode, context);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Warn("PURA-RENDER-901", $"增量更新失败，回退重建：{ex.Message}", newNode.GetType().Name);
        }

        return false;
    }

    private RenderContext CreateContext(
        PuraMarkdownOptions options,
        SanitizeResult sanitized,
        bool isStreaming,
        bool isTail,
        IReadOnlyDictionary<MarkdownObject, IReadOnlyList<HighlightSpan>>? precomputedHighlights = null)
    {
        var theme = ResolveTheme(options);
        if (!ReferenceEquals(_appliedTheme, theme))
        {
            _appliedTheme = theme;
            Background = new SolidColorBrush(theme.Colors.Background);
        }

        return new RenderContext
        {
            Theme = theme,
            Options = options,
            Renderers = options.Pipeline.Environment.Renderers,
            Highlighter = options.Pipeline.Environment.Highlighter,
            Sanitized = sanitized,
            Dispatcher = Dispatcher.UIThread,
            IsStreaming = isStreaming,
            IsTail = isTail,
            PrecomputedHighlights = precomputedHighlights,
            ComponentStates = _componentStates,
            InteractionSink = OnInteraction,
            LinkClickSink = args => LinkClicked?.Invoke(this, args),
            ImageLoadingSink = url => ImageLoading?.Invoke(this, new PuraImageEventArgs(url)),
            ImageLoadedSink = (url, result) => ImageLoaded?.Invoke(this, new PuraImageLoadedEventArgs(url, result)),
            WarningSink = args => Warning?.Invoke(this, args),
        };
    }

    private static TextBlock BlockedPlaceholder(PuraMarkdownOptions options)
        => new()
        {
            Text = "[内容因安全策略/预算限制被省略]",
            TextWrapping = TextWrapping.Wrap,
            FontSize = options.Theme.Typography.BodySize - 1,
            Foreground = new SolidColorBrush(options.Theme.Colors.Muted),
            Margin = new Thickness(0, 0, 0, options.Theme.Spacing.BlockSpacing),
        };

    private void WarnSanitizer(string warning)
    {
        var separator = warning.IndexOf(": ", StringComparison.Ordinal);
        if (warning.StartsWith("PURA-", StringComparison.Ordinal) && separator > 0)
        {
            Warn(warning[..separator], warning[(separator + 2)..]);
        }
        else
        {
            Warn("PURA-SEC-001", warning);
        }
    }

    private void OnInteraction(PuraInteractionEventArgs state)
    {
        if (!string.IsNullOrEmpty(state.ComponentId))
        {
            _componentStates[state.ComponentId] = state;
        }

        InteractionOccurred?.Invoke(this, state);
    }

    private void RemoveTailRecords()
    {
        if (_virtualized)
        {
            RemoveVirtualTail();
            return;
        }

        for (var i = 0; i < _tailRecordCount && _records.Count > 0; i++)
        {
            var last = _records[^1];
            if (last.View is { } view)
            {
                _host.Children.Remove(view);
            }

            _records.RemoveAt(_records.Count - 1);
        }

        _tailRecordCount = 0;
    }

    private void EnsureVirtualized(bool enabled)
    {
        if (_virtualized == enabled)
        {
            return;
        }

        _virtualized = enabled;
        if (enabled)
        {
            _host.Children.Clear();
            _root.Children.Remove(_host);
            if (!_root.Children.Contains(_virtualScroll))
            {
                _root.Children.Insert(0, _virtualScroll);
            }
        }
        else
        {
            _virtualBlocks.Clear();
            _root.Children.Remove(_virtualScroll);
            if (!_root.Children.Contains(_host))
            {
                _root.Children.Insert(0, _host);
            }
        }
    }

    private void RenderVirtualDocument(
        MarkdownDocument document,
        string text,
        RenderContext context,
        bool isStreaming)
    {
        _virtualBlocks.Clear();
        _records.Clear();

        foreach (var block in document)
        {
            var hash = HashOf(text, block);
            _virtualBlocks.Add(new VirtualBlock(block, hash, block.Span.Start, block.Span.End, context));
            _records.Add(new BlockRecord(_records.Count, block.Span.Start, block.Span.End, hash, block, null));
        }

        _lastRenderWasStreaming = isStreaming;
    }

    private void ApplyVirtualTail(StreamingWindowUpdate window, RenderContext context)
    {
        RemoveVirtualTail();

        foreach (var block in window.Blocks)
        {
            var hash = HashOf(window.SourceText, block);
            _virtualBlocks.Add(new VirtualBlock(
                block,
                hash,
                window.Offset + block.Span.Start,
                window.Offset + block.Span.End,
                context));
            _records.Add(new BlockRecord(
                _records.Count,
                window.Offset + block.Span.Start,
                window.Offset + block.Span.End,
                hash,
                block,
                null));
        }

        _tailRecordCount = window.Blocks.Count;
    }

    private void RemoveVirtualTail()
    {
        for (var i = 0; i < _tailRecordCount && _records.Count > 0; i++)
        {
            _records.RemoveAt(_records.Count - 1);
            if (_virtualBlocks.Count > 0)
            {
                _virtualBlocks.RemoveAt(_virtualBlocks.Count - 1);
            }
        }

        _tailRecordCount = 0;
    }

    private Control RenderVirtualBlock(VirtualBlock block)
    {
        if (block.Context.Sanitized.IsBlocked(block.Node))
        {
            return BlockedPlaceholder(block.Context.Options);
        }

        try
        {
            return block.Context.RenderMarkdown(block.Node);
        }
        catch (Exception ex)
        {
            Warn("PURA-RENDER-900", $"块渲染异常，降级为文本：{ex.Message}", block.Node.GetType().Name);
            return new TextBlock
            {
                Text = $"[渲染失败: {block.Node.GetType().Name}] {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(block.Context.Theme.Colors.Danger),
                Margin = new Thickness(0, 0, 0, 8),
            };
        }
    }

    /// <summary>滚动到内容末尾（块级虚拟化模式使用内部 ScrollViewer）。</summary>
    public void ScrollToEnd()
    {
        if (_virtualized)
        {
            _virtualScroll.ScrollToEnd();
        }
    }

    private PuraTheme ResolveTheme(PuraMarkdownOptions options)
    {
        if (!options.FollowSystemTheme)
        {
            return options.Theme;
        }

        return ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? options.DarkTheme ?? PuraTheme.Dark
            : options.Theme;
    }

    private void AddFallbackText(string text)
    {
        _host.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Options?.Theme.Typography.MonoFont ?? FontFamily.Default,
        });
        _lastRenderWasStreaming = false;
    }

    private void Warn(string code, string message, string? nodeType = null, string? renderer = null)
    {
        var key = $"{code}|{message}|{nodeType}|{renderer}";
        if (!_warnedKeys.Add(key))
        {
            return;
        }

        Warning?.Invoke(this, new PuraWarningEventArgs(new PuraWarning(code, message, nodeType, renderer)));
    }

    internal void ReportWarning(string code, string message, string? nodeType = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Warn(code, message, nodeType);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Warn(code, message, nodeType));
        }
    }

    private static ulong HashOf(string text, MarkdownObject block)
    {
        var span = block.Span;
        var start = span.Start;
        var end = span.End;
        if (start < 0 || end < start || end >= text.Length)
        {
            return (ulong)(block.GetType().Name + block.Line).GetHashCode();
        }

        var hash = 14695981039346656037UL;
        for (var i = start; i <= end; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }

        return hash;
    }
}

/// <summary>块级虚拟化的条目标：保存节点、哈希与窗口渲染上下文，按需由 ItemTemplate 实例化控件。</summary>
internal sealed class VirtualBlock(
    MarkdownObject node,
    ulong hash,
    int start,
    int end,
    RenderContext context)
{
    public MarkdownObject Node { get; } = node;
    public ulong Hash { get; } = hash;
    public int Start { get; } = start;
    public int End { get; } = end;
    public RenderContext Context { get; } = context;
}



























