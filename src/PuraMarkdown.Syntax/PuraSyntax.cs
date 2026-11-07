using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;
using PuraMarkdown.Security;

namespace PuraMarkdown.Syntax;

/// <summary>诊断严重级别（对应 LSP DiagnosticSeverity）。</summary>
public enum PuraDiagnosticSeverity
{
    /// <summary>错误。</summary>
    Error,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>信息。</summary>
    Information,
    /// <summary>提示。</summary>
    Hint,
}

/// <summary>一条语法/安全诊断。</summary>
public sealed record PuraDiagnostic(
    string Code,
    PuraDiagnosticSeverity Severity,
    string Message,
    SourceSpan Span,
    string? NodeType = null);

/// <summary>标题大纲项。</summary>
public sealed record PuraHeading(int Level, string Text, string Anchor);

/// <summary>折叠范围（供 IDE 式折叠/大纲使用）。</summary>
public sealed record PuraFoldRange(int StartLine, int EndLine, string Kind);

/// <summary>文档大纲。</summary>
public sealed record PuraOutline(
    IReadOnlyList<PuraHeading> Headings,
    IReadOnlyList<PuraFoldRange> Folds);

/// <summary>
/// 语法层产出：Markdig AST + 旁路元数据（版本、诊断、大纲）。
/// 不复制节点；MarkdownDocument 就是语法树。
/// </summary>
public sealed record PuraDocument(
    int Version,
    string Text,
    MarkdownDocument Markdown,
    IReadOnlyList<PuraDiagnostic> Diagnostics,
    PuraOutline Outline,
    SanitizeResult? Sanitized = null);

/// <summary>文本增量（用于增量解析契约）。</summary>
public sealed record PuraTextChange(int Offset, int RemovedLength, string InsertedText);

/// <summary>冻结后的 Markdig 解析管道；进程级复用。</summary>
public sealed class PuraSyntaxPipeline
{
    internal PuraSyntaxPipeline(MarkdownPipeline markdownPipeline, PuraSyntaxOptions options)
    {
        MarkdownPipeline = markdownPipeline;
        Options = options;
    }

    /// <summary>Markdig 管道（高级用户可直接使用）。</summary>
    public MarkdownPipeline MarkdownPipeline { get; }

    /// <summary>默认语法选项（sanitizer/limits/url policy）。</summary>
    public PuraSyntaxOptions Options { get; }

    /// <summary>为单次解析创建选项（覆盖 limits/url policy，沿用 pipeline sanitizer）。</summary>
    public PuraSyntaxOptions CreateOptions(PuraLimits limits, IUrlPolicy urlPolicy)
        => new()
        {
            Limits = limits,
            UrlPolicy = urlPolicy,
            Sanitizer = Options.Sanitizer,
        };

    /// <summary>解析为 Markdig 语法树。</summary>
    public MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, MarkdownPipeline);

    /// <summary>解析并生成 PuraDocument（版本/诊断/大纲）。</summary>
    public PuraDocument ParseDocument(string markdown, int version = 0, PuraSyntaxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var document = Parse(markdown);
        var effective = options ?? Options;
        var sanitized = effective.Sanitizer.Sanitize(document, effective);
        return new PuraDocument(
            version,
            markdown,
            document,
            sanitized.Diagnostics,
            BuildOutline(document),
            sanitized);
    }

    /// <summary>
    /// 增量解析：对“末尾追加且前缀已稳定”的常见流式场景复用旧 AST 前缀；其它编辑回退全量解析。
    /// 注意：命中复用路径时 <paramref name="previous"/> 的 AST 会被消费，调用方不应继续使用旧文档。
    /// </summary>
    public PuraDocument ParseIncremental(PuraDocument previous, PuraTextChange change)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(change);
        var text = string.Concat(
            previous.Text.AsSpan(0, change.Offset),
            change.InsertedText,
            previous.Text.AsSpan(change.Offset + change.RemovedLength));

        if (change.Offset == previous.Text.Length
            && change.RemovedLength == 0
            && previous.Text.Length > 0
            && StreamingBlockScanner.FindStableBoundary(previous.Text, 0) == previous.Text.Length)
        {
            var tail = Parse(change.InsertedText);
            var merged = new MarkdownDocument();
            var oldBlocks = previous.Markdown.ToArray();
            foreach (var block in oldBlocks)
            {
                previous.Markdown.Remove(block);
                merged.Add(block);
            }

            var charOffset = previous.Text.Length;
            var lineOffset = CountLines(previous.Text);
            foreach (var block in tail.ToArray())
            {
                Shift(block, charOffset, lineOffset);
                tail.Remove(block);
                merged.Add(block);
            }

            var sanitized = Options.Sanitizer.Sanitize(merged, Options);
            return new PuraDocument(
                previous.Version + 1,
                text,
                merged,
                sanitized.Diagnostics,
                BuildOutline(merged),
                sanitized);
        }

        return ParseDocument(text, previous.Version + 1);
    }

    private static int CountLines(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static void Shift(Block block, int charOffset, int lineOffset)
    {
        foreach (var node in block.Descendants())
        {
            node.Span = new SourceSpan(node.Span.Start + charOffset, node.Span.End + charOffset);
            if (node is Block nested)
            {
                nested.Line += lineOffset;
            }
        }

        block.Span = new SourceSpan(block.Span.Start + charOffset, block.Span.End + charOffset);
        block.Line += lineOffset;
    }

    private static PuraOutline BuildOutline(MarkdownDocument document)
    {
        var headings = new List<PuraHeading>();
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var folds = new List<PuraFoldRange>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var text = GetInlineText(heading.Inline).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var baseAnchor = Slug(text);
            var anchor = baseAnchor;
            var suffix = 2;
            while (!anchors.Add(anchor))
            {
                anchor = $"{baseAnchor}-{suffix++}";
            }

            headings.Add(new PuraHeading(heading.Level, text, anchor));
        }

        foreach (var block in document)
        {
            if (block.Line >= 0 && block.Span.End > block.Span.Start)
            {
                folds.Add(new PuraFoldRange(block.Line, block.Line + block.Span.End - block.Span.Start, "block"));
            }
        }

        return new PuraOutline(headings, folds);
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendInline(child, builder);
        }

        return builder.ToString();
    }

    private static void AppendInline(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline lineBreak:
                builder.Append(lineBreak.IsHard ? '\n' : ' ');
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case LinkInline link:
                builder.Append(GetInlineText(link));
                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                {
                    AppendInline(child, builder);
                }

                break;
        }
    }

    private static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
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
}

/// <summary>构建 Markdig 解析管道；只在启动时使用。</summary>
public sealed class PuraSyntaxBuilder
{
    private readonly MarkdownPipelineBuilder _markdown = new();
    private readonly List<IMarkdownExtension> _extensions = [];
    private IAstSanitizer _sanitizer = new DefaultAstSanitizer();
    private PuraLimits _limits = new();
    private IUrlPolicy _urlPolicy = DefaultUrlPolicy.Instance;

    /// <summary>启用 CommonMark 基础能力（含精确源码位置）。</summary>
    public PuraSyntaxBuilder UseCommonMark()
    {
        _markdown.UsePreciseSourceLocation();
        return this;
    }

    /// <summary>启用 GFM：表格、任务列表、强调扩展、自动链接。</summary>
    public PuraSyntaxBuilder UseGfm()
    {
        _markdown
            .UsePipeTables()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseAutoLinks();
        return this;
    }

    /// <summary>注册 Markdig 扩展（自定义语法高级入口）。</summary>
    public PuraSyntaxBuilder UseMarkdownExtension(IMarkdownExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _extensions.Add(extension);
        return this;
    }

    /// <summary>设置 AST 清洗器。</summary>
    public PuraSyntaxBuilder UseSanitizer(IAstSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        _sanitizer = sanitizer;
        return this;
    }

    /// <summary>配置安全/规模预算。</summary>
    public PuraSyntaxBuilder ConfigureLimits(Action<PuraLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_limits);
        return this;
    }

    /// <summary>设置 URL 策略。</summary>
    public PuraSyntaxBuilder UseUrlPolicy(IUrlPolicy urlPolicy)
    {
        ArgumentNullException.ThrowIfNull(urlPolicy);
        _urlPolicy = urlPolicy;
        return this;
    }

    /// <summary>构建不可变语法管道（关闭 HTML、启用精确 Span）。</summary>
    public PuraSyntaxPipeline Build()
    {
        foreach (var extension in _extensions)
        {
            _markdown.Extensions.Add(extension);
        }

        _markdown
            .UsePreciseSourceLocation()
            .DisableHtml();

        return new PuraSyntaxPipeline(_markdown.Build(), new PuraSyntaxOptions
        {
            Sanitizer = _sanitizer,
            Limits = _limits,
            UrlPolicy = _urlPolicy,
        });
    }
}

/// <summary>B 阶段的语法流式会话：稳定边界 + 已提交/尾部窗口 + 完整文档。</summary>
public sealed class PuraSyntaxStreamingSession
{
    private readonly PuraSyntaxPipeline _pipeline;
    private readonly PuraSyntaxOptions _options;
    private readonly StringBuilder _text = new();
    private int _committedOffset;
    private int _version;

    /// <summary>创建语法流式会话。</summary>
    public PuraSyntaxStreamingSession(PuraSyntaxPipeline pipeline, PuraSyntaxOptions? options = null)
    {
        _pipeline = pipeline;
        _options = options ?? pipeline.Options;
    }

    /// <summary>当前完整文档（兼容入口）。</summary>
    public PuraDocument? Current { get; private set; }

    /// <summary>已提交窗口文档。</summary>
    public PuraDocument? Committed { get; private set; }

    /// <summary>尾部窗口文档。</summary>
    public PuraDocument? Tail { get; private set; }

    /// <summary>已提交字符偏移。</summary>
    public int CommittedOffset => _committedOffset;

    /// <summary>追加文本：先按稳定边界提交，再解析尾部窗口。</summary>
    public void Append(string delta)
    {
        _text.Append(delta);
        var snapshot = _text.ToString();
        var boundary = StreamingBlockScanner.FindStableBoundary(snapshot, _committedOffset);
        if (boundary > _committedOffset)
        {
            Committed = _pipeline.ParseDocument(snapshot[..boundary], _version++, _options);
            _committedOffset = boundary;
        }

        Tail = _pipeline.ParseDocument(snapshot[_committedOffset..], _version++, _options);
        Current = _pipeline.ParseDocument(snapshot, _version++, _options);
    }

    /// <summary>完成并返回最终文档。</summary>
    public PuraDocument Complete()
    {
        Current = _pipeline.ParseDocument(_text.ToString(), _version++, _options);
        Committed = Current;
        _committedOffset = _text.Length;
        Tail = null;
        return Current;
    }
}



