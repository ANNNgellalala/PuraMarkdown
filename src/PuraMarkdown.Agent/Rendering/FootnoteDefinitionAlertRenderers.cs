using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using PuraMarkdown.Agent.Localization;

namespace PuraMarkdown.Agent.Rendering;

/// <summary>GitHub Alerts（> [!NOTE] 等）渲染。</summary>
internal sealed class AlertBlockRenderer : IPuraRenderer
{
    public bool CanRender(MarkdownObject node, RenderContext context) => node is AlertBlock;

    public Control Render(MarkdownObject node, RenderContext context)
    {
        var alert = (AlertBlock)node;
        var kind = alert.Kind.ToString().Trim().ToUpperInvariant();
        var localizer = context.Options.Localizer;
        var theme = context.Theme;
        var (label, color) = kind switch
        {
            "TIP" => (localizer.Get(PuraStringKeys.AlertTip), theme.Colors.Accent),
            "IMPORTANT" => (localizer.Get(PuraStringKeys.AlertImportant), theme.Colors.Accent),
            "WARNING" => (localizer.Get(PuraStringKeys.AlertWarning), theme.Colors.Danger),
            "CAUTION" => (localizer.Get(PuraStringKeys.AlertCaution), theme.Colors.Danger),
            _ => (localizer.Get(PuraStringKeys.AlertNote), theme.Colors.Accent),
        };

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            FontSize = theme.Typography.BodySize,
            Foreground = new SolidColorBrush(color),
        });

        foreach (var child in alert)
        {
            panel.Children.Add(context.RenderMarkdown(child));
        }

        return new Border
        {
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, theme.Spacing.BlockSpacing),
            Child = panel,
        };
    }
}

/// <summary>定义列表：DefinitionList/DefinitionItem/DefinitionTerm。</summary>
internal sealed class DefinitionListRenderer : IPuraRenderer
{
    public bool CanRender(MarkdownObject node, RenderContext context) => node is DefinitionList;

    public Control Render(MarkdownObject node, RenderContext context)
    {
        var list = (DefinitionList)node;
        var panel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing),
        };

        foreach (var item in list.OfType<DefinitionItem>())
        {
            var group = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var child in item)
            {
                if (child is DefinitionTerm term)
                {
                    var text = PuraTextBlockFactory.Create(context, wrap: true);
                    text.FontWeight = FontWeight.SemiBold;
                    context.RenderInlineChildren(term.Inline, text.Inlines!);
                    group.Children.Add(text);
                }
                else
                {
                    group.Children.Add(context.RenderMarkdown(child));
                }
            }

            panel.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(context.Theme.Colors.Border),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(8, 0, 0, 0),
                Child = group,
            });
        }

        return panel;
    }
}

/// <summary>脚注区（FootnoteGroup）与回链目标。</summary>
internal sealed class FootnoteGroupRenderer : IPuraRenderer
{
    public bool CanRender(MarkdownObject node, RenderContext context) => node is FootnoteGroup;

    public Control Render(MarkdownObject node, RenderContext context)
    {
        var group = (FootnoteGroup)node;
        var panel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, context.Theme.Spacing.BlockSpacing),
        };

        panel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(context.Theme.Colors.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var footnote in group.OfType<Footnote>())
        {
            var anchor = $"footnote-{footnote.Order}";
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock
            {
                Text = $"[{footnote.Order}]",
                FontSize = context.Theme.Typography.BodySize - 1,
                Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
                FontFamily = context.Theme.Typography.MonoFont,
            });

            foreach (var child in footnote)
            {
                content.Children.Add(context.RenderMarkdown(child));
            }

            var container = new Border
            {
                Tag = anchor,
                Child = content,
                Padding = new Thickness(0, 2),
            };
            context.AnchorSink?.Invoke(anchor, container);
            panel.Children.Add(container);
        }

        return panel;
    }
}
