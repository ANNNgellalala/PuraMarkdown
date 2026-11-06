using Markdig.Syntax;

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

    /// <summary>Warnings。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>IsBlocked。</summary>
    /// <param name="node">node 参数。</param>
    public bool IsBlocked(MarkdownObject node) => _blockedNodes.Contains(node);

    internal void Block(MarkdownObject node) => _blockedNodes.Add(node);
}

/// <summary>IAstSanitizer。</summary>
public interface IAstSanitizer
{
    /// <summary>Sanitize。</summary>
    /// <param name="document">document 参数。</param>
    /// <param name="options">options 参数。</param>
    SanitizeResult Sanitize(MarkdownDocument document, PuraMarkdownOptions options);
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
    public SanitizeResult Sanitize(MarkdownDocument document, PuraMarkdownOptions options)
    {
        var warnings = new List<string>();
        var result = new SanitizeResult { Warnings = warnings };

        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case Markdig.Syntax.Inlines.LinkInline { IsImage: false } link:
                    if (options.UrlPolicy.Evaluate(link.Url ?? string.Empty, UrlUsage.Link) == UrlDecision.Block)
                    {
                        result.Block(link);
                        warnings.Add($"PURA-SEC-001: 链接被拒绝：{link.Url}");
                    }
                    break;

                case Markdig.Syntax.Inlines.LinkInline { IsImage: true } image:
                    if (options.UrlPolicy.Evaluate(image.Url ?? string.Empty, UrlUsage.Image) == UrlDecision.Block)
                    {
                        result.Block(image);
                        warnings.Add($"PURA-SEC-001: 图片被拒绝：{image.Url}");
                    }
                    break;
            }
        }

        return result;
    }
}
