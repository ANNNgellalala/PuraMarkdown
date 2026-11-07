using System.Runtime.CompilerServices;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>Agent 组件集合：内置 choice/confirm + 插件追加的组件。</summary>
internal sealed class AgentComponentSet
{
    private readonly List<IAgentComponentParser> _parsers = [];
    private readonly List<IAgentComponentRenderer> _renderers = [];

    public AgentComponentSet()
    {
        Add(new ChoiceParser(), new ChoiceComponentRenderer());
        Add(new ConfirmParser(), new ConfirmComponentRenderer());
    }

    public bool PluginRegistered { get; set; }

    public void Add(IAgentComponentParser parser, IAgentComponentRenderer renderer)
    {
        _parsers.Add(parser);
        _renderers.Add(renderer);
    }

    public PuraComponentRegistry CreateRegistry() => new(_parsers, _renderers);
}

/// <summary>Agent 插件注册辅助：多个插件可向同一 `pura-agent` 围栏追加组件。</summary>
public static class AgentPipelineExtensions
{
    private static readonly ConditionalWeakTable<PuraPipelineBuilder, AgentComponentSet> Sets = new();

    /// <summary>启用内置 Agent 组件（重复调用安全）。</summary>
    public static PuraPipelineBuilder UseAgentComponents(this PuraPipelineBuilder builder)
    {
        var set = Sets.GetValue(builder, _ => new AgentComponentSet());
        EnsurePlugin(builder, set);
        return builder;
    }

    /// <summary>向 Agent 围栏追加自定义组件（自动确保 AgentPlugin 已注册）。</summary>
    /// <param name="builder">管道构建器。</param>
    /// <param name="parser">组件解析器。</param>
    /// <param name="renderer">组件渲染器。</param>
    public static PuraPipelineBuilder UseAgentComponent(
        this PuraPipelineBuilder builder,
        IAgentComponentParser parser,
        IAgentComponentRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(renderer);
        var set = Sets.GetValue(builder, _ => new AgentComponentSet());
        set.Add(parser, renderer);
        EnsurePlugin(builder, set);
        return builder;
    }

    private static void EnsurePlugin(PuraPipelineBuilder builder, AgentComponentSet set)
    {
        if (set.PluginRegistered)
        {
            return;
        }

        set.PluginRegistered = true;
        builder.UsePlugin(new AgentPlugin(set));
    }
}
