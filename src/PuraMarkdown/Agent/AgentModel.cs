using Avalonia.Controls;
using Json.Schema;
using Markdig.Syntax;

namespace PuraMarkdown.Agent;

/// <summary>Agent 组件模型基类（由 `pura-agent` 围栏 JSON 解析而来）。</summary>
public abstract record PuraAgentComponent
{
    /// <summary>组件类型（如 <c>choice</c>、<c>confirm</c> 或插件自定义类型）。</summary>
    public required string Type { get; init; }

    /// <summary>载荷 schema 版本（如 <c>1.0</c>）。</summary>
    public required string Version { get; init; }

    /// <summary>组件实例 ID；缺省时由所在块位置生成。</summary>
    public string? Id { get; init; }
}

/// <summary>选择组件的一个选项。</summary>
/// <param name="Id">选项 ID（回传值）。</param>
/// <param name="Label">选项显示名。</param>
/// <param name="Description">选项补充说明。</param>
/// <param name="Disabled">是否禁用。</param>
public sealed record PuraChoiceOption(string Id, string Label, string? Description = null, bool Disabled = false);

/// <summary>单选/多选组件模型。</summary>
public sealed record PuraChoiceComponent : PuraAgentComponent
{
    /// <summary>问题文本。</summary>
    public required string Question { get; init; }

    /// <summary>问题补充说明。</summary>
    public string? Description { get; init; }

    /// <summary>是否多选。</summary>
    public bool Multi { get; init; }

    /// <summary>选项列表。</summary>
    public required IReadOnlyList<PuraChoiceOption> Options { get; init; }

    /// <summary>默认选中项 ID。</summary>
    public IReadOnlyList<string> DefaultSelected { get; init; } = [];

    /// <summary>多选最少选择数量。</summary>
    public int Min { get; init; }

    /// <summary>多选最多选择数量。</summary>
    public int? Max { get; init; }

    /// <summary>提交按钮文案。</summary>
    public string SubmitLabel { get; init; } = "确认";

    /// <summary>组件级只读开关。</summary>
    public bool Disabled { get; init; }
}

/// <summary>二选一确认组件模型。</summary>
public sealed record PuraConfirmComponent : PuraAgentComponent
{
    /// <summary>确认问题文本。</summary>
    public required string Question { get; init; }

    /// <summary>确认补充说明。</summary>
    public string? Description { get; init; }

    /// <summary>确认按钮文案。</summary>
    public string ConfirmLabel { get; init; } = "确认";

    /// <summary>取消按钮文案。</summary>
    public string CancelLabel { get; init; } = "取消";

    /// <summary>是否使用危险样式。</summary>
    public bool Danger { get; init; }
}

/// <summary>组件载荷校验错误（消息 + JSON 路径）。</summary>
/// <param name="Message">错误消息。</param>
/// <param name="Path">JSON Pointer 风格的字段路径。</param>
public sealed record PuraComponentError(string Message, string? Path = null);

/// <summary>Agent 组件载荷解析器：按 <c>type</c> 把 JSON 解析为组件模型。</summary>
public interface IAgentComponentParser
{
    /// <summary>该解析器负责的组件类型。</summary>
    string ComponentType { get; }

    /// <summary>载荷 JSON Schema，用于统一校验与诊断。</summary>
    JsonSchema PayloadSchema { get; }

    /// <summary>尝试解析 JSON；失败时返回首条错误（含字段路径）。</summary>
    /// <param name="json">原始 JSON 文本。</param>
    /// <param name="component">解析成功后的组件模型。</param>
    /// <param name="error">解析失败时的首条错误。</param>
    /// <returns>是否解析成功。</returns>
    bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error);
}

/// <summary>组件渲染器：由核心按 <see cref="ComponentType"/> 查找并调用。</summary>
public interface IAgentComponentRenderer
{
    /// <summary>该渲染器负责的组件类型。</summary>
    string ComponentType { get; }

    /// <summary>把组件模型渲染为 Avalonia 控件。</summary>
    /// <param name="component">解析后的组件模型。</param>
    /// <param name="context">渲染上下文。</param>
    /// <returns>组件控件。</returns>
    Control RenderComponent(PuraAgentComponent component, Rendering.RenderContext context);
}

/// <summary>组件类型到解析器/渲染器的注册表（构建期冻结）。</summary>
public sealed class PuraComponentRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentComponentParser> _parsers;
    private readonly IReadOnlyDictionary<string, IAgentComponentRenderer> _renderers;

    internal PuraComponentRegistry(
        IEnumerable<IAgentComponentParser> parsers,
        IEnumerable<IAgentComponentRenderer> renderers)
    {
        _parsers = parsers
            .GroupBy(p => p.ComponentType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        _renderers = renderers
            .GroupBy(r => r.ComponentType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>已注册组件类型。</summary>
    public IReadOnlyList<string> ComponentTypes => _parsers.Keys.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>按组件类型解析载荷解析器。</summary>
    /// <param name="componentType">组件类型。</param>
    /// <returns>解析器；未注册时返回 <c>null</c>。</returns>
    public IAgentComponentParser? ResolveParser(string componentType)
        => _parsers.GetValueOrDefault(componentType);

    /// <summary>按组件类型解析组件渲染器。</summary>
    /// <param name="componentType">组件类型。</param>
    /// <returns>渲染器；未注册时返回 <c>null</c>。</returns>
    public IAgentComponentRenderer? ResolveRenderer(string componentType)
        => _renderers.GetValueOrDefault(componentType);
}



