using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Markdig.Syntax;
using PuraMarkdown.Localization;
using PuraMarkdown.Rendering;
using PuraMarkdown.Security;

namespace PuraMarkdown;

/// <summary>已提交/已渲染的块记录：流式增量嫁接与未来 IR/虚拟树的输入。</summary>
public sealed record BlockRecord(
    int Index,
    int Start,
    int End,
    ulong Hash,
    Markdig.Syntax.MarkdownObject Node,
    Control? View);

/// <summary>流式会话状态（终态不可逆）。</summary>
public enum PuraStreamingState
{
    /// <summary>已创建，尚未追加内容。</summary>
    Created,
    /// <summary>流式中。</summary>
    Streaming,
    /// <summary>已完成（全量校正后）。</summary>
    Completed,
    /// <summary>已中断（保留已渲染内容，不可续写）。</summary>
    Aborted,
}

/// <summary>流式选项：块复用、尾部窗口上限与同步模式。</summary>
public sealed class PuraStreamingOptions
{

    /// <summary>尾部窗口上限（字符）；超过时仅在稳定边界强制提交。</summary>
    public int TailWindowChars { get; init; } = 16 * 1024;

    /// <summary>
    /// 尾部硬上限（字符）。未闭合的超大块超过此值后暂停尾部刷新（避免 O(n²) 解析/渲染），
    /// 保留最后一次可见内容，Complete 时全量校正。默认 1MB。
    /// </summary>
    public int MaxTailChars { get; init; } = 1024 * 1024;

    /// <summary>启用块级虚拟化时，流式追加后自动滚动到底部（默认 true）。</summary>
    public bool AutoScrollToEnd { get; init; } = true;

    /// <summary>
    /// 同步模式：<see cref="PuraStreamingSession.Append"/> 内联完成解析与渲染。
    /// 测试/无 UI 消息循环的控制台场景使用；UI 应用应保持默认的异步后台解析 + UI 合帧。
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool Synchronous { get; init; }
}

/// <summary>流式统计（供诊断/测试）。</summary>
public sealed record PuraStreamingStatistics(
    int FlushCount,
    int Commits,
    int TotalChars,
    int CommittedChars,
    int TailChars,
    int TotalParsedChars,
    int CommittedParsedChars,
    int TailParsedChars,
    int LastParsedChars);

/// <summary>一个窗口（稳定前缀或尾部）的解析结果，可在后台线程构建、在 UI 线程渲染。</summary>
internal sealed record StreamingWindowUpdate(
    int Version,
    string SourceText,
    int Offset,
    MarkdownDocument Document,
    SanitizeResult Sanitized,
    IReadOnlyList<MarkdownObject> Blocks,
    IReadOnlyDictionary<MarkdownObject, IReadOnlyList<HighlightSpan>> Highlights,
    bool IsTail);

/// <summary>一次流式刷新：稳定窗口 + 尾部窗口。</summary>
internal sealed record PreparedDocument(
    string Text,
    MarkdownDocument Document,
    SanitizeResult Sanitized,
    IReadOnlyDictionary<MarkdownObject, IReadOnlyList<HighlightSpan>> Highlights,
    IReadOnlyList<PuraMarkdown.Rendering.PuraWarning> Warnings);

internal sealed record StreamingUpdate(
    int SessionId,
    int Version,
    IReadOnlyList<StreamingWindowUpdate> CommittedWindows,
    StreamingWindowUpdate? TailWindow,
    PuraStreamingStatistics Statistics);

/// <summary>
/// append-only 流式会话：后台/同步解析稳定块与尾部窗口，UI 线程只做控件嫁接。
/// 解析不复制全文（尾部 StringBuilder + committed 偏移），尾部块按内容哈希增量复用；
/// Complete 时做一次全量校正（保证与一次性渲染等价）。
/// </summary>
public sealed class PuraStreamingSession : IAsyncDisposable
{
    private readonly PuraMarkdownView _view;
    private readonly PuraStreamingOptions _options;
    private readonly PuraMarkdownOptions? _markdownOptions;
    private readonly int _sessionId;
    private readonly StringBuilder _full = new();
    private readonly StringBuilder _tail = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly ConcurrentDictionary<(string Language, ulong Hash), IReadOnlyList<HighlightSpan>> _highlightCache = new();
    private readonly Dictionary<int, (long At, int Length, IReadOnlyList<HighlightSpan> Spans)> _tailHighlights = new();
    private readonly StringBuilder _definitions = new();
    private readonly HashSet<string> _definitionLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private PuraStreamingState _state = PuraStreamingState.Created;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private int _committedOffset;
    private int _version;
    private bool _closed;
    private Exception? _lastWorkerError;
    private bool _tailOversizedWarned;
    private readonly List<TaskCompletionSource> _flushWaiters = [];
    private int _flushCount;
    private int _commits;
    private int _totalParsedChars;
    private int _committedParsedChars;
    private int _tailParsedChars;
    private int _lastParsedChars;

    internal PuraStreamingSession(PuraMarkdownView view, PuraStreamingOptions options)
    {
        _view = view;
        _options = options;
        _markdownOptions = view.Options;
        _sessionId = view.BeginStreamingRender();
    }

    /// <summary>诊断：后台 worker 最近一次异常。</summary>
    public Exception? LastWorkerError => _lastWorkerError;

    /// <summary>完成事件（全量校正渲染完成后触发）。</summary>
    public event EventHandler? Completed;

    /// <summary>中断事件（保留内容渲染完成后触发）。</summary>
    public event EventHandler? Aborted;

    /// <summary>待处理字符数（已接收未提交/未渲染的尾部窗口）。</summary>
    public int PendingChars
    {
        get
        {
            lock (_gate)
            {
                return _tail.Length;
            }
        }
    }

    /// <summary>当前完整文本（含已提交与尾部窗口）。</summary>
    public string CurrentText
    {
        get
        {
            lock (_gate)
            {
                return _full.ToString();
            }
        }
    }

    /// <summary>当前会话状态。</summary>
    public PuraStreamingState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>流式统计（提交次数、解析量、尾部窗口大小等）。</summary>
    public PuraStreamingStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return SnapshotStatistics();
            }
        }
    }

    /// <summary>追加增量文本。默认立即返回，解析与渲染在后台/UI 合帧完成。</summary>
    /// <param name="delta">追加的文本片段。</param>
    public void Append(string delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        bool startWorker;
        lock (_gate)
        {
            if (_state is PuraStreamingState.Completed or PuraStreamingState.Aborted)
            {
                throw new InvalidOperationException($"流式会话已进入终态（{_state}），不能再 Append");
            }

            if (_state == PuraStreamingState.Created)
            {
                _state = PuraStreamingState.Streaming;
            }

            _full.Append(delta);
            _tail.Append(delta);

            startWorker = !_options.Synchronous && _worker is null;
            if (!_options.Synchronous && _signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }

        if (_options.Synchronous)
        {
            ProcessAndApply();
        }
        else if (startWorker)
        {
            _worker = Task.Run(WorkerLoopAsync);
        }
    }

    /// <summary>追加增量文本（异步形式，便于宿主背压/取消）。</summary>
    /// <param name="delta">追加的文本片段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AppendAsync(ReadOnlyMemory<char> delta, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Append(delta.ToString());
        return Task.CompletedTask;
    }

    /// <summary>等待当前缓冲的增量完成一次解析与 UI 应用（测试/截图/导出前使用）。</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            if (_options.Synchronous || _worker is null || _closed)
            {
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _flushWaiters.Add(waiter);
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }

            return waiter.Task;
        }
    }

    /// <summary>等待流式渲染空闲（<see cref="FlushAsync"/> 的别名）。</summary>
    public Task WaitForIdleAsync() => FlushAsync();

    /// <summary>完成流式：取消后台任务并做一次全量校正渲染（后台解析 + 分帧 UI 挂载）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task CompleteAsync(CancellationToken cancellationToken = default)
        => FinishAsync(PuraStreamingState.Completed, PuraStringKeys.StreamingCompleted, cancellationToken);

    /// <summary>中断流式：保留已有内容并切换为可用状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AbortAsync(CancellationToken cancellationToken = default)
        => FinishAsync(PuraStreamingState.Aborted, PuraStringKeys.StreamingAborted, cancellationToken);

    private async Task FinishAsync(PuraStreamingState finalState, string announcementKey, CancellationToken cancellationToken)
    {
        var announcement = _markdownOptions?.Localizer.Get(announcementKey) ?? announcementKey;
        var text = string.Empty;
        Task? alreadyFinished = null;
        lock (_gate)
        {
            if (_state is PuraStreamingState.Completed or PuraStreamingState.Aborted)
            {
                alreadyFinished = _finished.Task;
            }
            else
            {
                _state = finalState;
                _closed = true;
                text = _full.ToString();
            }
        }

        if (alreadyFinished is not null)
        {
            await alreadyFinished.ConfigureAwait(false);
            return;
        }

        CancelWorker();
        _view.MarkStreamingClosed(_version + 1);

        var options = _markdownOptions;
        if (_options.Synchronous || options is null)
        {
            DrainFlushWaiters();
            _view.RenderText(text, isStreaming: false);
            _view.Announce(announcement);
            RaiseFinished(finalState);
            return;
        }

        try
        {
            // 全量解析 + 代码高亮放后台，UI 线程只分帧挂载控件；允许宿主从非 UI 线程调用。
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await Task.Run(() => PrepareDocument(text, options), cancellationToken).ConfigureAwait(false);
            await RenderOnUiAsync(() => _view.RenderPreparedAsync(prepared)).ConfigureAwait(false);
        }
        catch
        {
            await RenderOnUiAsync(() =>
            {
                _view.RenderText(text, isStreaming: false);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        await RenderOnUiAsync(() =>
        {
            _view.Announce(announcement);
            RaiseFinished(finalState);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private void RaiseFinished(PuraStreamingState finalState)
    {
        DrainFlushWaiters();
        try
        {
            if (finalState == PuraStreamingState.Completed)
            {
                Completed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Aborted?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _finished.TrySetResult();
        }
    }

    private void DrainFlushWaiters()
    {
        List<TaskCompletionSource> waiters;
        lock (_gate)
        {
            waiters = [.. _flushWaiters];
            _flushWaiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult();
        }
    }

    private static async Task RenderOnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        await completion.Task.ConfigureAwait(false);
    }

    /// <summary>释放会话资源。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
        }

        CancelWorker();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常关闭。
            }
        }

        _signal.Dispose();
        _cts.Dispose();
    }

    private void CancelWorker()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放。
        }

        // 唤醒可能正在等待信号的 worker。
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // 已释放。
            }
        }
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
                if (_closed)
                {
                    return;
                }

                // 合帧：一次唤醒处理当前所有已到达的增量，UI 每轮最多应用一次。
                var update = BuildUpdate();
                if (update is null)
                {
                    DrainFlushWaiters();
                    continue;
                }

                await ApplyOnUiAsync(update).ConfigureAwait(false);
                DrainFlushWaiters();
                if (_closed)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            _lastWorkerError = ex;
            _view.ReportWarning("PURA-STREAM-001", $"流式后台处理失败：{ex.Message}", nameof(PuraStreamingSession));
        }
    }

    private async Task ApplyOnUiAsync(StreamingUpdate update)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _view.ApplyStreamingUpdate(update);
            if (_options.AutoScrollToEnd)
            {
                _view.ScrollToEnd();
            }

            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _view.ApplyStreamingUpdate(update);
                if (_options.AutoScrollToEnd)
                {
                    _view.ScrollToEnd();
                }
            }
            finally
            {
                completion.TrySetResult();
            }
        });
        await completion.Task.ConfigureAwait(false);
    }

    private void ProcessAndApply()
    {
        var update = BuildUpdate();
        if (update is not null)
        {
            _view.ApplyStreamingUpdate(update);
            if (_options.AutoScrollToEnd)
            {
                _view.ScrollToEnd();
            }
        }
    }

    private StreamingUpdate? BuildUpdate()
    {
        var options = _markdownOptions;
        if (options is null)
        {
            return null;
        }

        string snapshot;
        int version;
        int committed;
        lock (_gate)
        {
            if (_closed || _tail.Length == 0)
            {
                return null;
            }

            snapshot = _tail.ToString();
            version = _version;
            committed = _committedOffset;
        }

        var cut = StreamingBlockScanner.FindStableBoundary(snapshot, 0, options.Pipeline.Syntax.MathEnabled);

        if (cut == 0 && snapshot.Length > _options.MaxTailChars)
        {
            var warn = false;
            lock (_gate)
            {
                if (!_tailOversizedWarned)
                {
                    _tailOversizedWarned = true;
                    warn = true;
                }
            }

            if (warn)
            {
                _view.ReportWarning(
                    "PURA-STREAM-002",
                    $"未闭合块超过 {_options.MaxTailChars} 字符，已暂停尾部刷新，完成后全量渲染",
                    nameof(PuraStreamingSession));
            }

            return null;
        }

        if (cut < snapshot.Length && snapshot.Length > _options.TailWindowChars)
        {
            // 尾部超过窗口上限时，只有在"前缀本身可稳定解析"的位置强制提交；
            // 否则可能把未闭合围栏/表格/列表从中间切断，造成重复或错乱块。
            var forced = snapshot.LastIndexOf("\n\n", StringComparison.Ordinal);
            if (forced >= 0)
            {
                var candidate = forced + 2;
                if (StreamingBlockScanner.FindStableBoundary(snapshot[..candidate], 0, options.Pipeline.Syntax.MathEnabled) == candidate)
                {
                    cut = candidate;
                }
            }
        }

        StreamingWindowUpdate? committedWindow = null;
        if (cut > 0)
        {
            committedWindow = ParseWindow(snapshot[..cut], committed, version, isTail: false, options);
        }

        StreamingWindowUpdate? tailWindow = null;
        if (cut < snapshot.Length)
        {
            tailWindow = ParseWindow(snapshot[cut..], committed + cut, version, isTail: true, options);
        }

        lock (_gate)
        {
            if (cut > 0)
            {
                _tail.Remove(0, cut);
                _committedOffset += cut;
                _commits++;
                _committedParsedChars += cut;
                _totalParsedChars += cut;
            }

            if (tailWindow is not null)
            {
                _tailParsedChars += tailWindow.SourceText.Length;
                _totalParsedChars += tailWindow.SourceText.Length;
                _lastParsedChars = tailWindow.SourceText.Length;
            }
            else
            {
                _lastParsedChars = 0;
            }

            _flushCount++;
            _version++;

            return new StreamingUpdate(
                _sessionId,
                version,
                committedWindow is null ? [] : [committedWindow],
                tailWindow,
                SnapshotStatistics());
        }
    }

    private StreamingWindowUpdate ParseWindow(
        string text,
        int offset,
        int version,
        bool isTail,
        PuraMarkdownOptions options)
    {
        // 已提交窗口中的引用式链接定义需要参与后续窗口解析，否则流式期间 [ref] 无法解析。
        var definitions = _definitions.ToString();
        var definitionsLength = definitions.Length;
        var source = definitionsLength == 0 ? text : definitions + text;
        var parsed = options.Pipeline.ParseDocument(source, 0, options.CreateSyntaxOptions());
        var document = parsed.Markdown;
        var sanitized = parsed.Sanitized
            ?? options.Pipeline.Syntax.Options.Sanitizer.Sanitize(document, options.CreateSyntaxOptions());
        var blocks = document.ToArray()
            .Where(block => block is not Markdig.Syntax.LinkReferenceDefinitionGroup)
            .ToArray();
        ExtractDefinitions(document);
        var highlights = ComputeHighlights(document, options, isTail, offset - definitionsLength);
        return new StreamingWindowUpdate(
            version,
            source,
            offset - definitionsLength,
            document,
            sanitized,
            blocks,
            highlights,
            isTail);
    }

    private void ExtractDefinitions(MarkdownDocument document)
    {
        foreach (var definition in document.Descendants<LinkReferenceDefinition>())
        {
            var label = definition.Label;
            var url = definition.Url;
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url)
                || !_definitionLabels.Add(label))
            {
                continue;
            }

            _definitions.Append('[').Append(label).Append("]: <").Append(url).Append('>');
            if (!string.IsNullOrEmpty(definition.Title))
            {
                _definitions.Append(" \"").Append(definition.Title).Append('"');
            }

            _definitions.Append('\n');
        }
    }

    private IReadOnlyDictionary<MarkdownObject, IReadOnlyList<HighlightSpan>> ComputeHighlights(
        MarkdownDocument document,
        PuraMarkdownOptions options,
        bool isTail,
        int offset)
    {
        var highlighter = options.Pipeline.Environment.Highlighter;
        var result = new Dictionary<MarkdownObject, IReadOnlyList<HighlightSpan>>();
        foreach (var codeBlock in document.Descendants<CodeBlock>())
        {
            var language = CodeBlockRenderer.GetLanguage(codeBlock);
            if (language is null || !highlighter.CanHighlight(language))
            {
                continue;
            }

            try
            {
                var code = MarkdownText.GetLeafText(codeBlock).TrimEnd('\n');
                if (isTail)
                {
                    var blockKey = offset + codeBlock.Span.Start;
                    var now = Stopwatch.GetTimestamp();
                    if (_tailHighlights.TryGetValue(blockKey, out var previous)
                        && (code.Length - previous.Length < 256
                            || Stopwatch.GetElapsedTime(previous.At).TotalMilliseconds < 100))
                    {
                        // 节流：沿用上一份前缀高亮，新增字符先以纯文本显示，100ms 内最多重算一次。
                        result[codeBlock] = previous.Spans;
                        continue;
                    }

                    var spans = _highlightCache.GetOrAdd(
                        (language, Fnv1a(code)),
                        _ => highlighter.Highlight(code, language));
                    _tailHighlights[blockKey] = (now, code.Length, spans);
                    result[codeBlock] = spans;
                    continue;
                }

                result[codeBlock] = _highlightCache.GetOrAdd(
                    (language, Fnv1a(code)),
                    _ => highlighter.Highlight(code, language));
            }
            catch
            {
                // 高亮失败时回退纯文本；渲染器会处理缺失的预计算。
            }
        }

        if (_highlightCache.Count > 1024)
        {
            _highlightCache.Clear();
        }

        if (_tailHighlights.Count > 64)
        {
            _tailHighlights.Clear();
        }

        return result;
    }

    private static ulong Fnv1a(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private PreparedDocument PrepareDocument(string text, PuraMarkdownOptions options)
    {
        var warnings = new List<PuraMarkdown.Rendering.PuraWarning>();
        if (text.Length > options.Limits.MaxDocumentChars)
        {
            warnings.Add(new PuraMarkdown.Rendering.PuraWarning(
                "PURA-LIM-001",
                $"文档超过 {options.Limits.MaxDocumentChars} 字符，已截断"));
            text = text[..options.Limits.MaxDocumentChars];
        }

        var parsed = options.Pipeline.ParseDocument(text, 0, options.CreateSyntaxOptions());
        var document = parsed.Markdown;
        var sanitized = parsed.Sanitized
            ?? options.Pipeline.Syntax.Options.Sanitizer.Sanitize(document, options.CreateSyntaxOptions());
        foreach (var warning in sanitized.Warnings)
        {
            warnings.Add(new PuraMarkdown.Rendering.PuraWarning("PURA-SEC-001", warning));
        }

        var highlights = ComputeHighlights(document, options, isTail: false, offset: 0);
        return new PreparedDocument(text, document, sanitized, highlights, warnings);
    }

    private PuraStreamingStatistics SnapshotStatistics()
        => new(
            _flushCount,
            _commits,
            _full.Length,
            _committedOffset,
            _tail.Length,
            _totalParsedChars,
            _committedParsedChars,
            _tailParsedChars,
            _lastParsedChars);
}

























