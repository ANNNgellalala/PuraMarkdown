namespace PuraMarkdown.Agent.Rendering;

/// <summary>默认高亮器：不支持任何语言，渲染为纯等宽文本。</summary>
public sealed class NullCodeHighlighter : ICodeHighlighter
{
    /// <summary>Instance。</summary>
    public static NullCodeHighlighter Instance { get; } = new();

    /// <summary>CanHighlight。</summary>
    /// <param name="language">language 参数。</param>
    public bool CanHighlight(string? language) => false;

    /// <summary>Highlight。</summary>
    /// <param name="code">code 参数。</param>
    /// <param name="language">language 参数。</param>
    public IReadOnlyList<HighlightSpan> Highlight(string code, string? language) => [];
}
