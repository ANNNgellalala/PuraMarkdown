using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Markdig.Syntax;

namespace PuraMarkdown.Rendering;

/// <summary>
/// 把 Markdig 节点渲染为 Avalonia 控件。实现必须是 UI 线程安全的（只在 UI 线程调用）。
/// </summary>
public interface IPuraRenderer
{
    /// 必须廉价、确定性、无副作用；可能在后台线程被调用。
    bool CanRender(MarkdownObject node, RenderContext context);

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    Control Render(MarkdownObject node, RenderContext context);
}

/// <summary>支持"节点类型不变、内容变化"时就地更新控件（流式尾部块）。</summary>
public interface IIncrementalPuraRenderer : IPuraRenderer
{
    /// <summary>CanUpdate。</summary>
    /// <param name="oldNode">oldNode 参数。</param>
    /// <param name="newNode">newNode 参数。</param>
    /// <param name="context">context 参数。</param>
    bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context);

    /// <summary>Update。</summary>
    /// <param name="existing">existing 参数。</param>
    /// <param name="newNode">newNode 参数。</param>
    /// <param name="context">context 参数。</param>
    void Update(Control existing, MarkdownObject newNode, RenderContext context);
}

/// <summary>行内扩展节点的渲染器。</summary>
public interface IPuraInlineRenderer : IPuraRenderer
{
    /// <summary>RenderInline。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    Inline RenderInline(MarkdownObject node, RenderContext context);
}

/// <summary>围栏渲染器：接管 ```lang 代码围栏（图表、Agent、数学、自定义块）。</summary>
public interface IFenceRenderer
{
    /// <summary>诊断名称（构建期唯一）。</summary>
    string Name { get; }

    /// <summary>是否处理该语言围栏；<paramref name="context"/>.IsTail 表示流式未完成。</summary>
    bool CanRender(string language, string source, bool isClosed, RenderContext context);

    /// <summary>渲染围栏；<paramref name="isClosed"/> 表示围栏是否已闭合。</summary>
    Control Render(string language, string source, bool isClosed, RenderContext context);
}

/// <summary>围栏渲染器注册项（内部使用）。</summary>
internal sealed record FenceRendererRegistration(IFenceRenderer Renderer, int Priority);

/// <summary>代码高亮策略。默认实现为纯等宽文本（NullCodeHighlighter）。</summary>
public interface ICodeHighlighter
{
    /// <summary>CanHighlight。</summary>
    /// <param name="language">language 参数。</param>
    bool CanHighlight(string? language);

    /// <summary>Highlight。</summary>
    /// <param name="code">code 参数。</param>
    /// <param name="language">language 参数。</param>
    IReadOnlyList<HighlightSpan> Highlight(string code, string? language);
}

/// <summary>struct。</summary>
/// <param name="Start">Start 参数。</param>
/// <param name="Length">Length 参数。</param>
/// <param name="Token">Token 参数。</param>
public readonly record struct HighlightSpan(int Start, int Length, string Token);


