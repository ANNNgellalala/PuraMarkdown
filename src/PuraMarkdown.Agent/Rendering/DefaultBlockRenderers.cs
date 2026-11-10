using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Agent.Rendering;

internal sealed class ParagraphRenderer : IIncrementalPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is ParagraphBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var paragraph = (ParagraphBlock)node;
        var textBlock = PuraTextBlockFactory.Create(context, wrap: true);
        textBlock.TextWrapping = TextWrapping.Wrap;
        textBlock.Foreground = new SolidColorBrush(context.Theme.Colors.Text);
        textBlock.FontSize = context.Theme.Typography.BodySize;
        textBlock.FontFamily = context.Theme.Typography.BodyFont;
        textBlock.Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing);

        DefaultInlineRenderer.RenderChildren(paragraph.Inline, textBlock.Inlines!, context);
        return textBlock;
    }

    /// <summary>CanUpdate。</summary>
    public bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context)
        => oldNode is ParagraphBlock && newNode is ParagraphBlock;

    /// <summary>Update。</summary>
    public void Update(Control existing, MarkdownObject newNode, RenderContext context)
    {
        if (existing is not TextBlock textBlock || newNode is not ParagraphBlock paragraph)
        {
            return;
        }

        textBlock.Inlines!.Clear();
        DefaultInlineRenderer.RenderChildren(paragraph.Inline, textBlock.Inlines!, context);
    }
}

internal sealed class HeadingRenderer : IIncrementalPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is HeadingBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var heading = (HeadingBlock)node;
        var textBlock = PuraTextBlockFactory.Create(context, wrap: true);
        textBlock.TextWrapping = TextWrapping.Wrap;
        textBlock.FontSize = context.Theme.Typography.HeadingSize(heading.Level);
        textBlock.FontWeight = FontWeight.Bold;
        textBlock.Foreground = new SolidColorBrush(context.Theme.Colors.Text);
        textBlock.FontFamily = context.Theme.Typography.BodyFont;
        textBlock.Margin = new Thickness(0, heading.Level == 1 ? 12 : 8, 0, 4);

        DefaultInlineRenderer.RenderChildren(heading.Inline, textBlock.Inlines!, context);
        AutomationProperties.SetHeadingLevel(textBlock, Math.Clamp(heading.Level, 1, 6));
        return textBlock;
    }

    /// <summary>CanUpdate。</summary>
    public bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context)
        => oldNode is HeadingBlock oldHeading
           && newNode is HeadingBlock newHeading
           && oldHeading.Level == newHeading.Level;

    /// <summary>Update。</summary>
    public void Update(Control existing, MarkdownObject newNode, RenderContext context)
    {
        if (existing is not TextBlock textBlock || newNode is not HeadingBlock heading)
        {
            return;
        }

        textBlock.Inlines!.Clear();
        DefaultInlineRenderer.RenderChildren(heading.Inline, textBlock.Inlines!, context);
    }
}

internal sealed class ThematicBreakRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is ThematicBreakBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
        => new Border
        {
            Height = 1,
            Background = new SolidColorBrush(context.Theme.Colors.Border),
            Margin = new Thickness(0, 8, 0, 12),
        };
}

internal sealed class QuoteRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is QuoteBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var quote = (QuoteBlock)node;
        var stack = new StackPanel { Spacing = 2 };
        foreach (var child in quote)
        {
            stack.Children.Add(context.RenderMarkdown(child));
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(context.Theme.Colors.Border),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(context.Theme.Spacing.QuoteIndent, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing),
            Child = stack,
        };
    }
}

internal sealed class ListRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is ListBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var list = (ListBlock)node;
        var panel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing),
        };

        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var parsedStart))
        {
            number = parsedStart;
        }
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
            {
                continue;
            }

            ParagraphBlock? taskParagraph = null;
            TaskList? taskList = null;
            foreach (var child in listItem)
            {
                if (child is ParagraphBlock paragraph && paragraph.Inline?.FirstChild is TaskList task)
                {
                    taskParagraph = paragraph;
                    taskList = task;
                    break;
                }
            }

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            Control marker;
            if (taskList is not null)
            {
                marker = new CheckBox
                {
                    IsChecked = taskList.Checked,
                    IsEnabled = false,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
            }
            else
            {
                marker = new TextBlock
                {
                    Text = list.IsOrdered ? $"{number}." : "•",
                    FontSize = context.Theme.Typography.BodySize,
                    Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
                    Margin = new Thickness(0, 0, 6, 0),
                    MinWidth = 14,
                };
            }

            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var content = new StackPanel { Spacing = 1 };
            Grid.SetColumn(content, 1);
            row.Children.Add(content);

            foreach (var child in listItem)
            {
                if (ReferenceEquals(child, taskParagraph))
                {
                    var taskText = PuraTextBlockFactory.Create(context, wrap: true);
                    taskText.TextWrapping = TextWrapping.Wrap;
                    taskText.FontSize = context.Theme.Typography.BodySize;
                    taskText.Foreground = new SolidColorBrush(context.Theme.Colors.Text);
                    taskText.FontFamily = context.Theme.Typography.BodyFont;
                    DefaultInlineRenderer.RenderChildren(taskParagraph.Inline, taskText.Inlines!, context, skipFirst: true);
                    content.Children.Add(taskText);
                }
                else
                {
                    content.Children.Add(context.RenderMarkdown(child));
                }
            }

            panel.Children.Add(row);
            if (list.IsOrdered)
            {
                number++;
            }
        }

        return panel;
    }
}

internal sealed class HtmlFallbackRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is HtmlBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
        => new TextBlock
        {
            Text = MarkdownText.GetLeafText((HtmlBlock)node),
            FontFamily = context.Theme.Typography.MonoFont,
            FontSize = context.Theme.Typography.BodySize - 1,
            Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing),
        };
}

internal sealed class FallbackTextRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => true;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
        => new TextBlock
        {
            Text = $"[未支持节点: {node.GetType().Name}]",
            FontSize = context.Theme.Typography.BodySize - 1,
            Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
            TextWrapping = TextWrapping.Wrap,
        };
}





