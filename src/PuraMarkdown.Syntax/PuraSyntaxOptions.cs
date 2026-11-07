using PuraMarkdown.Security;

namespace PuraMarkdown.Syntax;

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


