using Avalonia.Controls;
using Markdig.Syntax;
using PuraMarkdown.Localization;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>Agent 语法插件：接管 `pura-agent` 围栏。</summary>
public sealed class AgentPlugin : IPuraPlugin
{
    private readonly AgentComponentSet _components;

    /// <summary>创建内置 choice/confirm 的 Agent 插件。</summary>
    public AgentPlugin()
        : this(new AgentComponentSet())
    {
    }

    internal AgentPlugin(AgentComponentSet components)
    {
        _components = components;
    }

    /// <summary>创建 Agent 插件，可追加自定义组件解析器/渲染器。</summary>
    /// <param name="parsers">自定义解析器（追加到内置之后）。</param>
    /// <param name="renderers">自定义渲染器（追加到内置之后）。</param>
    public AgentPlugin(
        IEnumerable<IAgentComponentParser>? parsers,
        IEnumerable<IAgentComponentRenderer>? renderers)
        : this(new AgentComponentSet())
    {
        var rendererList = (renderers ?? []).ToArray();
        foreach (var parser in parsers ?? [])
        {
            var renderer = rendererList.FirstOrDefault(candidate =>
                string.Equals(candidate.ComponentType, parser.ComponentType, StringComparison.OrdinalIgnoreCase));
            if (renderer is not null)
            {
                _components.Add(parser, renderer);
            }
        }
    }

    /// <inheritdoc/>
    public string Name => "pura.agent";

    /// <inheritdoc/>
    public string Version => "1.0.0";

    /// <inheritdoc/>
    public string? Description => "pura-agent 围栏与 JSON 交互组件（choice/confirm）";

    /// <inheritdoc/>
    public void Configure(PuraPipelineBuilder builder)
    {
        builder.UseFenceRenderer(new AgentFenceRenderer(_components));
    }
}

/// <summary>把 `pura-agent` 围栏解析为组件控件。</summary>
public sealed class AgentFenceRenderer : IFenceRenderer
{
    private readonly AgentComponentSet _components;

    internal AgentFenceRenderer(AgentComponentSet components)
    {
        _components = components;
    }

    /// <inheritdoc/>
    public string Name => "pura.agent.fence";

    /// <inheritdoc/>
    public bool CanRender(string language, string source, bool isClosed, RenderContext context)
        => string.Equals(language, "pura-agent", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Control Render(string language, string source, bool isClosed, RenderContext context)
    {
        if (!isClosed || (context.IsStreaming && context.IsTail))
        {
            return AgentCards.Loading(context.Theme, context.Options.Localizer.Get(PuraStringKeys.ComponentPreparing));
        }

        return AgentComponentRenderer.RenderPayload(source, context, _components.CreateRegistry());
    }
}

/// <summary>组件载荷渲染（从原核心 PuraAgentBlock 逻辑迁移为围栏实现）。</summary>
internal static class AgentComponentRenderer
{
    public static Control RenderPayload(string source, RenderContext context, PuraComponentRegistry registry)
    {
        var raw = source.Trim();
        var localizer = context.Options.Localizer;

        if (context.IsStreaming && context.IsTail)
        {
            return AgentCards.Loading(context.Theme, localizer.Get(PuraStringKeys.ComponentPreparing));
        }

        if (raw.Length == 0)
        {
            return AgentCards.Error(
                context.Theme,
                localizer.Get(PuraStringKeys.ComponentEmpty),
                localizer.Get(PuraStringKeys.ComponentEmptyDetail),
                raw);
        }

        if (raw.Length > context.Options.Limits.MaxFenceChars)
        {
            context.Warn("PURA-LIM-004", $"组件载荷超过 {context.Options.Limits.MaxFenceChars} 字符", nameof(AgentFenceRenderer));
            return AgentCards.Error(
                context.Theme,
                localizer.Get(PuraStringKeys.ComponentTooLarge),
                localizer.Get(PuraStringKeys.ComponentTooLargeDetail, context.Options.Limits.MaxFenceChars),
                string.Empty);
        }

        string? type;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentMustBeObject), "$", raw);
            }

            if (!AgentJson.TryGetType(root, out type) || string.IsNullOrWhiteSpace(type))
            {
                return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentMissingType), "type", raw);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            context.Warn("PURA-DATA-001", $"pura-agent JSON 解析失败：{ex.Message}", nameof(AgentFenceRenderer));
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentJsonParseFailed), ex.Message, raw);
        }

        var parser = registry.ResolveParser(type);
        if (parser is null)
        {
            return AgentCards.Unsupported(context.Theme, localizer.Get(PuraStringKeys.UnsupportedComponent), type, raw);
        }

        if (!parser.TryParse(raw, out var component, out var error) || component is null)
        {
            context.Warn("PURA-DATA-002", $"组件 {type} 校验失败：{error?.Message}", nameof(AgentFenceRenderer));
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentValidationFailed), FormatError(error, localizer), raw);
        }

        if (!SchemaValidation.TryValidate(parser, raw, out var schemaError))
        {
            context.Warn("PURA-DATA-002", $"组件 {type} schema 校验失败：{schemaError}", nameof(AgentFenceRenderer));
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentSchemaFailed), schemaError, raw);
        }

        if (string.IsNullOrEmpty(component.Id))
        {
            component = component with { Id = $"pura-agent-{Fnv1a(raw):x}" };
        }

        var renderer = registry.ResolveRenderer(component.Type);
        if (renderer is null)
        {
            return AgentCards.Unsupported(context.Theme, localizer.Get(PuraStringKeys.UnsupportedComponent), component.Type, raw);
        }

        try
        {
            var control = renderer.RenderComponent(component, context);
            var stateId = component.Id!;
            if (control is IPuraInteractionStateReceiver receiver
                && context.ComponentStates?.TryGetValue(stateId, out var state) == true)
            {
                receiver.ApplyInteractionState(state);
            }

            return control;
        }
        catch (Exception ex)
        {
            context.Warn("PURA-RENDER-001", $"组件渲染失败：{ex.Message}", nameof(AgentFenceRenderer), renderer.GetType().Name);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentRenderFailed), ex.Message, raw);
        }
    }

    private static ulong Fnv1a(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static string FormatError(PuraComponentError? error, Localization.IPuraLocalizer localizer)
        => error is null
            ? localizer.Get(PuraStringKeys.ComponentUnknownValidation)
            : error.Path is null ? error.Message : $"{error.Path}: {error.Message}";
}

internal static class SchemaValidation
{
    public static bool TryValidate(IAgentComponentParser parser, string raw, out string error)
    {
        error = string.Empty;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            var options = new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.List };
            var result = parser.PayloadSchema.Evaluate(document.RootElement, options);
            if (result.IsValid)
            {
                return true;
            }

            error = FirstError(result) ?? "载荷不符合组件 schema";
            return false;
        }
        catch (Exception ex)
        {
            error = $"schema 校验异常：{ex.Message}";
            return false;
        }
    }

    private static string? FirstError(Json.Schema.EvaluationResults result)
    {
        if (result.Errors is { Count: > 0 })
        {
            var first = result.Errors.First();
            return $"{result.InstanceLocation}: {first.Value}";
        }

        foreach (var detail in result.Details ?? [])
        {
            var nested = FirstError(detail);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}







