using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Json.Schema;
using Markdig.Syntax;
using PuraMarkdown.Agent;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Plugins.Samples;

/// <summary>示例自定义 Agent 组件：rating（星级评分）。</summary>
public sealed record RatingComponent : PuraAgentComponent
{
    public string Label { get; init; } = "评分";

    public int Max { get; init; } = 5;

    public int Value { get; init; }
}

public sealed class RatingParser : IAgentComponentParser
{
    public string ComponentType => "rating";

    public JsonSchema PayloadSchema { get; } = JsonSchema.FromText("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["type", "version"],
          "properties": {
            "type": { "const": "rating" },
            "version": { "type": "string", "minLength": 1 },
            "id": { "type": "string", "maxLength": 64 },
            "label": { "type": "string", "maxLength": 200 },
            "max": { "type": "integer", "minimum": 1, "maximum": 10 },
            "value": { "type": "integer", "minimum": 0, "maximum": 10 }
          },
          "additionalProperties": true
        }
        """);

    public bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error)
    {
        component = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = new PuraComponentError($"JSON 语法错误：{ex.Message}");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = new PuraComponentError("载荷必须是对象", "$");
                return false;
            }

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "rating")
            {
                error = new PuraComponentError("type 必须为 rating", "type");
                return false;
            }

            var version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(version))
            {
                error = new PuraComponentError("缺少 version", "version");
                return false;
            }

            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var label = root.TryGetProperty("label", out var labelElement) ? labelElement.GetString() : null;

            var max = 5;
            if (root.TryGetProperty("max", out var maxElement))
            {
                if (!maxElement.TryGetInt32(out max) || max is < 1 or > 10)
                {
                    error = new PuraComponentError("max 必须是 1..10 的整数", "max");
                    return false;
                }
            }

            var value = 0;
            if (root.TryGetProperty("value", out var valueElement))
            {
                if (!valueElement.TryGetInt32(out value) || value is < 0 || value > max)
                {
                    error = new PuraComponentError($"value 必须在 0..{max}", "value");
                    return false;
                }
            }

            component = new RatingComponent
            {
                Type = "rating",
                Version = version,
                Id = id,
                Label = string.IsNullOrWhiteSpace(label) ? "评分" : label,
                Max = max,
                Value = value,
            };
            return true;
        }
    }
}

public sealed class RatingRenderer : IAgentComponentRenderer
{
    public string ComponentType => "rating";

    public Control RenderComponent(PuraAgentComponent component, MarkdownObject node, RenderContext context)
    {
        var model = (RatingComponent)component;
        var componentId = model.Id ?? $"pura-agent-{node.Line}";
        return new RatingControl(model, context, componentId);
    }
}

/// <summary>rating 控件：支持在虚拟化回收/重渲染后恢复已上报的评分状态。</summary>
public sealed class RatingControl : Border, IPuraInteractionStateReceiver
{
    private readonly RatingComponent _model;
    private readonly RenderContext _context;
    private readonly string _componentId;
    private readonly List<Button> _buttons = [];

    public RatingControl(RatingComponent model, RenderContext context, string componentId)
    {
        _model = model;
        _context = context;
        _componentId = componentId;

        var theme = context.Theme;
        var enabled = context.Options.InteractionEnabled && !context.IsStreaming;
        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(new TextBlock
        {
            Text = model.Label,
            FontSize = theme.Typography.BodySize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(theme.Colors.Text),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var i = 1; i <= model.Max; i++)
        {
            var value = i;
            var button = new Button
            {
                Content = value.ToString(),
                IsEnabled = enabled,
                Padding = new Thickness(8, 2),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(theme.Colors.Accent),
                BorderBrush = new SolidColorBrush(theme.Colors.Accent),
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(button, $"{model.Label} {value}");
            button.Click += (_, _) =>
            {
                foreach (var other in _buttons)
                {
                    other.IsEnabled = false;
                }

                context.InteractionSink?.Invoke(new PuraInteractionEventArgs
                {
                    ComponentType = "rating",
                    ComponentId = _componentId,
                    SelectedIds = [value.ToString()],
                });
            };
            _buttons.Add(button);
            buttons.Children.Add(button);
        }

        root.Children.Add(buttons);
        BorderBrush = new SolidColorBrush(theme.Colors.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12);
        Child = root;
        AutomationProperties.SetName(this, model.Label);
    }

    /// <summary>恢复已上报的评分状态。</summary>
    /// <param name="state">交互事件参数。</param>
    public void ApplyInteractionState(PuraInteractionEventArgs state)
    {
        var selected = state.SelectedIds.FirstOrDefault();
        foreach (var button in _buttons)
        {
            button.IsEnabled = false;
            button.FontWeight = string.Equals(button.Content?.ToString(), selected, StringComparison.Ordinal)
                ? FontWeight.Bold
                : FontWeight.Normal;
        }
    }
}

public sealed class RatingPlugin : IPuraPlugin
{
    public string Name => "acme.rating";

    public string Version => "1.0.0";

    public string? Description => "示例：自定义 Agent 组件 rating";

    public void Configure(PuraPipelineBuilder builder)
    {
        builder.UseAgentComponentParser(new RatingParser());
        builder.UseAgentComponentRenderer(new RatingRenderer());
    }
}

/// <summary>示例图表渲染器：验证 IDiagramRenderer 契约（真实 Mermaid 引擎在 M2 后续 spike 接入）。</summary>
public sealed class SampleDiagramRenderer : IDiagramRenderer
{
    public bool CanRenderDiagram(string language, string source, RenderContext context)
        => string.Equals(language, "mermaid", StringComparison.OrdinalIgnoreCase);

    public Control RenderDiagram(string language, string source, RenderContext context)
    {
        var theme = context.Theme;
        var preview = source.Length > 160 ? source[..160] + "…" : source;
        return new Border
        {
            BorderBrush = new SolidColorBrush(theme.Colors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Mermaid（示例插件渲染）",
                        FontSize = theme.Typography.BodySize - 1,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(theme.Colors.Accent),
                    },
                    new TextBlock
                    {
                        Text = preview,
                        FontSize = theme.Typography.BodySize - 2,
                        FontFamily = theme.Typography.MonoFont,
                        Foreground = new SolidColorBrush(theme.Colors.Muted),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
    }
}

public sealed class SampleDiagramPlugin : IPuraPlugin
{
    public string Name => "pura.samples.diagram";

    public string Version => "1.0.0";

    public string? Description => "示例图表插件（占位实现，验证 IDiagramRenderer）";

    public void Configure(PuraPipelineBuilder builder) => builder.UseDiagramRenderer(new SampleDiagramRenderer());
}

