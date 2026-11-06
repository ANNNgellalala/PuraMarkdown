using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Markdig.Syntax;
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
    /// <summary>开启后，渲染器可用块级复用减少重建（默认开启）。</summary>
    public bool EnableBlockReuse { get; init; } = true;

    /// <summary>尾部窗口上限（字符）；超过时在最近空行强制提交。</summary>
    public int TailWindowChars { get; init; } = 16 * 1024;

    /// <summary>
    /// 同步模式：<see cref="PuraStreamingSession.Append"/> 内联完成解析与渲染。
    /// 测试/无 UI 消息循环的控制台场景使用；UI 应用应保持默认的异步后台解析 + UI 合帧。
    /// </summary>
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
    private readonly CancellationTokenSource _cts = new();
    private PuraStreamingState _state = PuraStreamingState.Created;
    private Task? _worker;
    private int _committedOffset;
    private int _version;
    private bool _closed;
    private Exception? _lastWorkerError;
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

    /// <summary>完成流式：取消后台任务并做一次全量校正渲染。</summary>
    public async Task CompleteAsync()
    {
        string text;
        lock (_gate)
        {
            if (_state is PuraStreamingState.Completed or PuraStreamingState.Aborted)
            {
                return;
            }

            _state = PuraStreamingState.Completed;
            _closed = true;
            text = _full.ToString();
        }

        CancelWorker();
        _view.MarkStreamingClosed(_version + 1);

        var options = _markdownOptions;
        if (_options.Synchronous || options is null)
        {
            _view.RenderText(text, isStreaming: false);
            _view.Announce("消息内容生成完成");
            return;
        }

        try
        {
            // 全量解析 + 代码高亮放后台，UI 线程只分帧挂载控件。
            var prepared = await Task.Run(() => PrepareDocument(text, options)).ConfigureAwait(true);
            await _view.RenderPreparedAsync(prepared).ConfigureAwait(true);
        }
        catch
        {
            _view.RenderText(text, isStreaming: false);
        }

        _view.Announce("消息内容生成完成");
    }

    /// <summary>中断流式：保留已有内容并切换为可用状态。</summary>
    public Task AbortAsync()
    {
        string text;
        lock (_gate)
        {
            if (_state is PuraStreamingState.Completed or PuraStreamingState.Aborted)
            {
                return Task.CompletedTask;
            }

            _state = PuraStreamingState.Aborted;
            _closed = true;
            text = _full.ToString();
        }

        CancelWorker();
        _view.MarkStreamingClosed(_version + 1);
        _view.RenderText(text, isStreaming: false);
        _view.Announce("消息生成已中断");
        return Task.CompletedTask;
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
                    continue;
                }

                await ApplyOnUiAsync(update).ConfigureAwait(false);
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
        }
    }

    private async Task ApplyOnUiAsync(StreamingUpdate update)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _view.ApplyStreamingUpdate(update);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _view.ApplyStreamingUpdate(update);
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

        var cut = StreamingBlockScanner.FindStableBoundary(snapshot, 0);
        if (cut < snapshot.Length && snapshot.Length > _options.TailWindowChars)
        {
            // 尾部超过窗口上限：在最近空行强制提交；Complete 的全量校正兜底。
            var forced = snapshot.LastIndexOf("\n\n", StringComparison.Ordinal);
            if (forced >= 0)
            {
                cut = forced + 2;
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
        var document = options.Pipeline.Parse(text);
        var sanitized = options.Pipeline.Environment.Sanitizer.Sanitize(document, options);
        var highlights = ComputeHighlights(document, options, isTail, offset);
        return new StreamingWindowUpdate(
            version,
            text,
            offset,
            document,
            sanitized,
            document.ToArray(),
            highlights,
            isTail);
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

        var document = options.Pipeline.Parse(text);
        var sanitized = options.Pipeline.Environment.Sanitizer.Sanitize(document, options);
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











