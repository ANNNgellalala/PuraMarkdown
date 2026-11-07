using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Json.Schema;
using Markdig.Syntax;
using PuraMarkdown.Localization;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>
/// `pura-agent` 围栏的统一入口：解析 JSON → schema 校验 → 组件解析器 → 组件渲染器。
/// 任何一步失败都渲染降级卡，不影响其它块；流式未闭合时显示占位。
/// </summary>
internal sealed class AgentComponentRenderer : IPuraRenderer
{
    /// <summary>DefaultPriority。</summary>
    public const int DefaultPriority = 100;

    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is PuraAgentBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var agentBlock = (PuraAgentBlock)node;
        var localizer = context.Options.Localizer;

        if (!agentBlock.IsClosed)
        {
            return AgentCards.Loading(context.Theme, localizer.Get(PuraStringKeys.ComponentLoading));
        }

        // 流式尾部窗口：不执行 JSON 解析/Schema 校验等重型工作，等块稳定提交后再渲染。
        if (context.IsStreaming && context.IsTail)
        {
            return AgentCards.Loading(context.Theme, localizer.Get(PuraStringKeys.ComponentPreparing));
        }

        var raw = MarkdownText.GetLeafText(agentBlock).Trim();
        var nodeType = agentBlock.GetType().Name;

        if (raw.Length == 0)
        {
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentEmpty), localizer.Get(PuraStringKeys.ComponentEmptyDetail), raw);
        }

        if (raw.Length > context.Options.Limits.MaxFenceChars)
        {
            context.Warn("PURA-LIM-004", $"组件载荷超过 {context.Options.Limits.MaxFenceChars} 字符", nodeType);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentTooLarge), localizer.Get(PuraStringKeys.ComponentTooLargeDetail, context.Options.Limits.MaxFenceChars), string.Empty);
        }

        string? type;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentMustBeObject), "$", raw);
            }

            if (!AgentJson.TryGetType(root, out type) || string.IsNullOrWhiteSpace(type))
            {
                return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentMissingType), "type", raw);
            }
        }
        catch (JsonException ex)
        {
            context.Warn("PURA-DATA-001", $"pura-agent JSON 解析失败：{ex.Message}", nodeType);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentJsonParseFailed), ex.Message, raw);
        }

        var parser = context.Components.ResolveParser(type);
        if (parser is null)
        {
            return AgentCards.Unsupported(context.Theme, localizer.Get(PuraStringKeys.UnsupportedComponent), type, raw);
        }

        if (!parser.TryParse(raw, out var component, out var error) || component is null)
        {
            context.Warn("PURA-DATA-002", $"组件 {type} 校验失败：{error?.Message}", nodeType);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentValidationFailed), FormatError(error, localizer), raw);
        }

        if (!SchemaValidation.TryValidate(parser, raw, out var schemaError))
        {
            context.Warn("PURA-DATA-002", $"组件 {type} schema 校验失败：{schemaError}", nodeType);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentSchemaFailed), schemaError, raw);
        }

        var renderer = context.Components.ResolveRenderer(component.Type);
        if (renderer is null)
        {
            return AgentCards.Unsupported(context.Theme, localizer.Get(PuraStringKeys.UnsupportedComponent), component.Type, raw);
        }

        try
        {
            return renderer.RenderComponent(component, agentBlock, context);
        }
        catch (Exception ex)
        {
            context.Warn("PURA-RENDER-001", $"组件渲染失败：{ex.Message}", nodeType, renderer.GetType().Name);
            return AgentCards.Error(context.Theme, localizer.Get(PuraStringKeys.ComponentRenderFailed), ex.Message, raw);
        }
    }

    private static string FormatError(PuraComponentError? error, IPuraLocalizer localizer)
        => error is null
            ? localizer.Get(PuraStringKeys.ComponentUnknownValidation)
            : error.Path is null ? error.Message : $"{error.Path}: {error.Message}";
}

internal static class SchemaValidation
{
    /// <summary>TryValidate。</summary>
    /// <param name="parser">parser 参数。</param>
    /// <param name="raw">raw 参数。</param>
    /// <param name="error">error 参数。</param>
    public static bool TryValidate(IAgentComponentParser parser, string raw, out string error)
    {
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
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

    private static string? FirstError(EvaluationResults result)
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

internal sealed class ChoiceComponentRenderer : IAgentComponentRenderer
{
    /// <summary>ComponentType。</summary>
    public string ComponentType => "choice";

    /// <summary>RenderComponent。</summary>
    /// <param name="component">component 参数。</param>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control RenderComponent(PuraAgentComponent component, MarkdownObject node, RenderContext context)
        => new PuraChoiceControl((PuraChoiceComponent)component, node, context);
}

internal sealed class ConfirmComponentRenderer : IAgentComponentRenderer
{
    /// <summary>ComponentType。</summary>
    public string ComponentType => "confirm";

    /// <summary>RenderComponent。</summary>
    /// <param name="component">component 参数。</param>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control RenderComponent(PuraAgentComponent component, MarkdownObject node, RenderContext context)
        => new PuraConfirmControl((PuraConfirmComponent)component, node, context);
}






