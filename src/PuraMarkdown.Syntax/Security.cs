using Markdig.Syntax;
using PuraMarkdown.Syntax;

namespace PuraMarkdown.Security;

/// <summary>URL 用途，用于策略区分链接/图片/其它资源。</summary>
public enum UrlUsage
{
    /// <summary>普通链接。</summary>
    Link,
    /// <summary>图片资源。</summary>
    Image,
    /// <summary>其它用途（如 artifact）。</summary>
    Other,
}

/// <summary>URL 策略判定结果。</summary>
public enum UrlDecision
{
    /// <summary>允许。</summary>
    Allow,
    /// <summary>拒绝并按文本降级。</summary>
    Block,
}

/// <summary>IUrlPolicy。</summary>
public interface IUrlPolicy
{
    /// <summary>Evaluate。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="usage">usage 参数。</param>
    UrlDecision Evaluate(string url, UrlUsage usage);
}

/// <summary>默认策略：仅允许 http/https/mailto 与相对路径/锚点；其余（javascript:、data: 等）拒绝。</summary>
public sealed class DefaultUrlPolicy : IUrlPolicy
{
    /// <summary>Instance。</summary>
    public static DefaultUrlPolicy Instance { get; } = new();

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto", "tel"];

    /// <summary>Evaluate。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="usage">usage 参数。</param>
    public UrlDecision Evaluate(string url, UrlUsage usage)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return UrlDecision.Block;
        }

        if (url.StartsWith('#'))
        {
            return UrlDecision.Allow;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // 相对路径：允许（是否可加载由宿主/ImageLoader 决定）
            return url.Contains(':', StringComparison.Ordinal) ? UrlDecision.Block : UrlDecision.Allow;
        }

        return AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? UrlDecision.Allow
            : UrlDecision.Block;
    }
}

/// <summary>解析/渲染预算。超限时降级并给出 warning，而不是抛异常。</summary>
public sealed class PuraLimits
{
    /// <summary>MaxDocumentChars。</summary>
    public int MaxDocumentChars { get; set; } = 4 * 1024 * 1024;

    /// <summary>MaxFenceChars。</summary>
    public int MaxFenceChars { get; set; } = 256 * 1024;

    /// <summary>MaxBlocks。</summary>
    public int MaxBlocks { get; set; } = 5000;

    /// <summary>MaxNestingDepth。</summary>
    public int MaxNestingDepth { get; set; } = 64;
}

/// <summary>SanitizeResult。</summary>
public sealed class SanitizeResult
{
    private readonly HashSet<MarkdownObject> _blockedNodes = [];
    private readonly List<string> _warnings = [];
    private readonly List<PuraDiagnostic> _diagnostics = [];

    /// <summary>清洗告警。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>结构化诊断（语法层可直接消费）。</summary>
    public IReadOnlyList<PuraDiagnostic> Diagnostics => _diagnostics;

    /// <summary>判断节点是否被清洗策略阻断。</summary>
    /// <param name="node">Markdown 节点。</param>
    public bool IsBlocked(MarkdownObject node) => _blockedNodes.Contains(node);

    /// <summary>阻断节点（自定义 sanitizer 使用）。</summary>
    /// <param name="node">要阻断的节点。</param>
    public void Block(MarkdownObject node) => _blockedNodes.Add(node);

    /// <summary>追加一条告警（自定义 sanitizer 使用）。</summary>
    /// <param name="warning">告警文本，建议以 PURA-* 开头。</param>
    /// <param name="node">关联节点（用于诊断 Span）。</param>
    public void AddWarning(string warning, MarkdownObject? node = null)
    {
        _warnings.Add(warning);
        var separator = warning.IndexOf(": ", StringComparison.Ordinal);
        var code = warning.StartsWith("PURA-", StringComparison.Ordinal) && separator > 0
            ? warning[..separator]
            : "PURA-SEC-001";
        var message = separator > 0 ? warning[(separator + 2)..] : warning;
        var span = node?.Span ?? new SourceSpan(0, 0);
        _diagnostics.Add(new PuraDiagnostic(code, PuraDiagnosticSeverity.Warning, message, span, node?.GetType().Name));
    }

    /// <summary>追加结构化诊断。</summary>
    public void AddDiagnostic(PuraDiagnostic diagnostic) => _diagnostics.Add(diagnostic);
}

/// <summary>一次解析/清洗的语法选项（安全预算、URL 策略、sanitizer）。</summary>
public sealed class PuraSyntaxOptions
{
    /// <summary>安全/规模预算。</summary>
    public PuraLimits Limits { get; init; } = new();

    /// <summary>URL 策略。</summary>
    public IUrlPolicy UrlPolicy { get; init; } = DefaultUrlPolicy.Instance;

    /// <summary>AST 清洗器。</summary>
    public IAstSanitizer Sanitizer { get; init; } = new DefaultAstSanitizer();
}

/// <summary>IAstSanitizer。</summary>
public interface IAstSanitizer
{
    /// <summary>Sanitize。</summary>
    /// <param name="document">document 参数。</param>
    /// <param name="options">options 参数。</param>
    SanitizeResult Sanitize(MarkdownDocument document, PuraSyntaxOptions options);
}

/// <summary>
/// 一次 AST 遍历：URL 策略、预算检查，把结论交给渲染器共享（补足"无 IR"后缺失的集中安全点）。
/// M0 只实现 URL 与文档长度；HTML 由 Markdig DisableHtml 在解析期处理。
/// </summary>
public sealed class DefaultAstSanitizer : IAstSanitizer
{
    /// <summary>Sanitize。</summary>
    /// <param name="document">document 参数。</param>
    /// <param name="options">options 参数。</param>
    public SanitizeResult Sanitize(MarkdownDocument document, PuraSyntaxOptions options)
    {
        var result = new SanitizeResult();
        var limits = options.Limits;
        var blocks = document.ToArray();

        if (blocks.Length > limits.MaxBlocks)
        {
            result.AddWarning($"PURA-LIM-005: 块数量超过 {limits.MaxBlocks}，超出部分不渲染");
            foreach (var block in blocks.Skip(limits.MaxBlocks))
            {
                result.Block(block);
            }
        }

        foreach (var block in blocks.Take(limits.MaxBlocks))
        {
            if (MaxDepth(block) > limits.MaxNestingDepth)
            {
                result.Block(block);
                result.AddWarning($"PURA-LIM-006: 嵌套深度超过 {limits.MaxNestingDepth}，已省略该块", block);
            }
        }

        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case Markdig.Syntax.Inlines.LinkInline { IsImage: false } link:
                    if (options.UrlPolicy.Evaluate(link.Url ?? string.Empty, UrlUsage.Link) == UrlDecision.Block)
                    {
                        result.Block(link);
                        result.AddWarning($"PURA-SEC-001: 链接被拒绝：{link.Url}", link);
                    }
                    break;

                case Markdig.Syntax.Inlines.LinkInline { IsImage: true } image:
                    if (options.UrlPolicy.Evaluate(image.Url ?? string.Empty, UrlUsage.Image) == UrlDecision.Block)
                    {
                        result.Block(image);
                        result.AddWarning($"PURA-SEC-001: 图片被拒绝：{image.Url}", image);
                    }
                    break;
            }
        }

        return result;
    }

    private static int MaxDepth(MarkdownObject node)
    {
        switch (node)
        {
            case Markdig.Syntax.ContainerBlock container:
            {
                var max = 0;
                foreach (var child in container)
                {
                    max = Math.Max(max, MaxDepth(child));
                }

                return max + 1;
            }

            case Markdig.Syntax.LeafBlock leaf when leaf.Inline is not null:
                return 1 + MaxDepth(leaf.Inline);

            case Markdig.Syntax.Inlines.ContainerInline inline:
            {
                var max = 0;
                for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
                {
                    max = Math.Max(max, MaxDepth(child));
                }

                return max + 1;
            }

            default:
                return 1;
        }
    }
}





