using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PuraMarkdown.Agent;
using PuraMarkdown.Security;

namespace PuraMarkdown.Rendering;

/// <summary>
/// 渲染上下文：主题、服务、注册表与组合辅助。只读服务包，不做 service locator。
/// </summary>
public sealed class RenderContext
{
    /// <summary>Theme。</summary>
    public required PuraTheme Theme { get; init; }
    /// <summary>Options。</summary>
    public required PuraMarkdownOptions Options { get; init; }
    /// <summary>Renderers。</summary>
    public required PuraRendererRegistry Renderers { get; init; }
    /// <summary>Components。</summary>
    public required PuraComponentRegistry Components { get; init; }
    /// <summary>Highlighter。</summary>
    public required ICodeHighlighter Highlighter { get; init; }
    /// <summary>Diagrams。</summary>
    public required IReadOnlyList<IDiagramRenderer> Diagrams { get; init; }
    /// <summary>Sanitized。</summary>
    public required SanitizeResult Sanitized { get; init; }
    /// <summary>Dispatcher。</summary>
    public required IDispatcher Dispatcher { get; init; }
    /// <summary>IsStreaming。</summary>
    public bool IsStreaming { get; init; }

    /// <summary>是否为流式尾部窗口（未稳定块）；尾部块应避免高亮/图表等重型渲染。</summary>
    public bool IsTail { get; init; }

    /// <summary>后台预计算的代码高亮 span（按节点索引）；流式渲染时避免在 UI 线程重复高亮。</summary>
    public IReadOnlyDictionary<MarkdownObject, IReadOnlyList<HighlightSpan>>? PrecomputedHighlights { get; init; }
    /// <summary>InteractionSink。</summary>
    public Action<PuraInteractionEventArgs>? InteractionSink { get; init; }
    /// <summary>WarningSink。</summary>
    public Action<PuraWarningEventArgs>? WarningSink { get; init; }

    /// <summary>RenderMarkdown。</summary>
    /// <param name="node">node 参数。</param>
    public Control RenderMarkdown(MarkdownObject node)
    {
        Exception? lastError = null;
        var attempts = 0;

        foreach (var renderer in Renderers.ResolveCandidates(node, this))
        {
            if (++attempts > 8)
            {
                break;
            }

            try
            {
                return renderer.Render(node, this);
            }
            catch (Exception ex)
            {
                lastError = ex;
                Warn("PURA-RENDER-010", $"渲染器 {renderer.GetType().Name} 渲染失败，尝试回退：{ex.Message}", node.GetType().Name, renderer.GetType().Name);
            }
        }

        throw new InvalidOperationException($"没有渲染器可以处理节点 {node.GetType().Name}", lastError);
    }

    /// <summary>RenderChildren。</summary>
    /// <param name="container">container 参数。</param>
    public IReadOnlyList<Control> RenderChildren(ContainerBlock container)
    {
        var list = new List<Control>();
        foreach (var child in container)
        {
            list.Add(RenderMarkdown(child));
        }
        return list;
    }

    /// <summary>RenderInlineChildren。</summary>
    /// <param name="container">container 参数。</param>
    /// <param name="target">target 参数。</param>
    public void RenderInlineChildren(ContainerInline? container, InlineCollection target)
        => DefaultInlineRenderer.RenderChildren(container, target, this);

    /// <summary>Warn。</summary>
    /// <param name="code">code 参数。</param>
    /// <param name="message">message 参数。</param>
    /// <param name="nodeType">nodeType 参数。</param>
    /// <param name="renderer">renderer 参数。</param>
    public void Warn(string code, string message, string? nodeType = null, string? renderer = null)
        => WarningSink?.Invoke(new PuraWarningEventArgs(new PuraWarning(code, message, nodeType, renderer)));
}

/// <summary>PuraWarning。</summary>
/// <param name="Code">Code 参数。</param>
/// <param name="Message">Message 参数。</param>
/// <param name="NodeType">NodeType 参数。</param>
/// <param name="Renderer">Renderer 参数。</param>
public sealed record PuraWarning(string Code, string Message, string? NodeType = null, string? Renderer = null);

/// <summary>PuraWarningEventArgs。</summary>
public sealed class PuraWarningEventArgs(PuraWarning warning) : EventArgs
{
    /// <summary>Warning。</summary>
    public PuraWarning Warning { get; } = warning;
}
