using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;

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
    PuraOutline Outline);

/// <summary>文本增量（用于增量解析契约）。</summary>
public sealed record PuraTextChange(int Offset, int RemovedLength, string InsertedText);

/// <summary>冻结后的 Markdig 解析管道；进程级复用。</summary>
public sealed class PuraSyntaxPipeline
{
    internal PuraSyntaxPipeline(MarkdownPipeline markdownPipeline)
    {
        MarkdownPipeline = markdownPipeline;
    }

    /// <summary>Markdig 管道（高级用户可直接使用）。</summary>
    public MarkdownPipeline MarkdownPipeline { get; }

    /// <summary>解析为 Markdig 语法树。</summary>
    public MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, MarkdownPipeline);

    /// <summary>解析并生成 PuraDocument（版本/诊断/大纲）。</summary>
    public PuraDocument ParseDocument(string markdown, int version = 0)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var document = Parse(markdown);
        return new PuraDocument(
            version,
            markdown,
            document,
            BuildDiagnostics(document),
            BuildOutline(document));
    }

    /// <summary>增量解析契约；B 阶段内部先全量解析，C 阶段可替换为真正增量。</summary>
    public PuraDocument ParseIncremental(PuraDocument previous, PuraTextChange change)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(change);
        var text = string.Concat(
            previous.Text.AsSpan(0, change.Offset),
            change.InsertedText,
            previous.Text.AsSpan(change.Offset + change.RemovedLength));
        return ParseDocument(text, previous.Version + 1);
    }

    private static IReadOnlyList<PuraDiagnostic> BuildDiagnostics(MarkdownDocument document)
    {
        // B 阶段：解析层诊断暂为空；安全/预算诊断仍由渲染层 sanitizer 产生。
        _ = document;
        return [];
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

        return new PuraSyntaxPipeline(_markdown.Build());
    }
}

/// <summary>B 阶段的简单语法流式会话；稳定边界/块复用后续可下沉到此。</summary>
public sealed class PuraSyntaxStreamingSession
{
    private readonly PuraSyntaxPipeline _pipeline;
    private readonly StringBuilder _text = new();

    /// <summary>创建语法流式会话。</summary>
    public PuraSyntaxStreamingSession(PuraSyntaxPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>当前解析文档。</summary>
    public PuraDocument? Current { get; private set; }

    /// <summary>追加文本（会重新解析，B 阶段语义等价）。</summary>
    public void Append(string delta)
    {
        _text.Append(delta);
        Current = _pipeline.ParseDocument(_text.ToString(), (Current?.Version ?? 0) + 1);
    }

    /// <summary>完成并返回最终文档。</summary>
    public PuraDocument Complete()
    {
        Current = _pipeline.ParseDocument(_text.ToString(), (Current?.Version ?? 0) + 1);
        return Current;
    }
}
