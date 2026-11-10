using Markdig.Syntax;

namespace PuraMarkdown.Agent.Rendering;

/// <summary>按优先级解析渲染器：priority 降序，首个 CanRender 胜出。</summary>
public sealed class PuraRendererRegistry
{
    private readonly IReadOnlyList<RendererRegistration> _registrations;

    internal PuraRendererRegistry(IEnumerable<RendererRegistration> registrations)
    {
        _registrations = registrations
            .OrderByDescending(r => r.Priority)
            .ToArray();
    }

    /// <summary>Registrations。</summary>
    public IReadOnlyList<IPuraNodeRenderer> Renderers => _registrations.Select(registration => registration.Renderer).ToArray();

    /// <summary>Resolve。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public IPuraNodeRenderer Resolve(MarkdownObject node, RenderContext context)
    {
        var candidates = ResolveCandidates(node, context);
        return candidates.Count > 0
            ? candidates[0]
            : throw new InvalidOperationException($"没有渲染器可以处理节点 {node.GetType().Name}（应注册 FallbackTextRenderer）");
    }

    /// <summary>返回所有能处理该节点的渲染器（按优先级），供回退链使用。</summary>
    public IReadOnlyList<IPuraNodeRenderer> ResolveCandidates(MarkdownObject node, RenderContext context)
    {
        var candidates = new List<IPuraNodeRenderer>();
        foreach (var registration in _registrations)
        {
            try
            {
                if (registration.Renderer.CanRender(node, context))
                {
                    candidates.Add(registration.Renderer);
                }
            }
            catch (Exception ex)
            {
                context.Warn("PURA-RENDER-000", $"渲染器 {registration.Renderer.GetType().Name} 的 CanRender 抛异常：{ex.Message}", node.GetType().Name, registration.Renderer.GetType().Name);
            }
        }

        return candidates;
    }

    /// <summary>ResolveInline。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public IPuraInlineRenderer? ResolveInline(MarkdownObject node, RenderContext context)
    {
        foreach (var registration in _registrations)
        {
            if (registration.Renderer is not IPuraInlineRenderer inlineRenderer)
            {
                continue;
            }

            try
            {
                if (inlineRenderer.CanRender(node, context))
                {
                    return inlineRenderer;
                }
            }
            catch (Exception ex)
            {
                context.Warn("PURA-RENDER-000", $"行内渲染器 {inlineRenderer.GetType().Name} 的 CanRender 抛异常：{ex.Message}", node.GetType().Name, inlineRenderer.GetType().Name);
            }
        }

        return null;
    }
}

/// <summary>RendererRegistration。</summary>
/// <param name="Renderer">Renderer 参数。</param>
/// <param name="Priority">Priority 参数。</param>
internal sealed record RendererRegistration(IPuraNodeRenderer Renderer, int Priority);




