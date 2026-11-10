using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace PuraMarkdown.Agent.Testing;

/// <summary>确定性控件树 dump：只输出结构/类型/文本/交互状态，用于 golden 与等价性测试。</summary>
public static class ControlTreeDumper
{
    /// <summary>Dump。</summary>
    /// <param name="root">root 参数。</param>
    public static string Dump(Control root)
    {
        var builder = new StringBuilder();
        Walk(root, 0, builder);
        return builder.ToString();
    }

    /// <summary>按类型查找控件（深度优先）。</summary>
    /// <typeparam name="T">目标控件类型。</typeparam>
    /// <param name="root">根控件。</param>
    /// <returns>匹配的控件序列。</returns>
    public static IEnumerable<T> Find<T>(Control root) where T : Control
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in Children(root))
        {
            foreach (var found in Find<T>(child))
            {
                yield return found;
            }
        }
    }

    private static void Walk(Control control, int depth, StringBuilder builder)
    {
        if (EqualityComparer<string?>.Default.Equals(control.Tag as string, "pura-live-region"))
        {
            return;
        }

        if (EqualityComparer<string?>.Default.Equals(control.Tag as string, "pura-root"))
        {
            foreach (var child in Children(control))
            {
                Walk(child, depth, builder);
            }

            return;
        }

        builder.Append(' ', depth * 2).Append(control.GetType().Name);

        switch (control)
        {
            case ToggleButton toggle:
                builder.Append(" checked=").Append(toggle.IsChecked).Append(" enabled=").Append(toggle.IsEnabled);
                break;
            case Button button:
                builder.Append(" content=\"").Append(button.Content).Append("\" enabled=").Append(button.IsEnabled);
                break;
            case TextBlock textBlock:
                builder.Append(" text=\"").Append(GetText(textBlock)).Append('"');
                break;
            case TextPresenter presenter:
                builder.Append(" text=\"").Append(presenter.Text).Append('"');
                break;
        }

        builder.Append('\n');

        foreach (var child in Children(control))
        {
            Walk(child, depth + 1, builder);
        }
    }

    private static IEnumerable<Control> Children(Control control)
    {
        switch (control)
        {
            case Panel panel:
                return panel.Children.OfType<Control>();
            case TextBlock { Inlines: { Count: > 0 } inlines }:
                return inlines.OfType<InlineUIContainer>().Select(container => container.Child).OfType<Control>();
            case Border { Child: Control borderChild }:
                return [borderChild];
            case ScrollViewer { Content: Control scrollChild }:
                return [scrollChild];
            case ContentControl { Content: Control contentChild }:
                return [contentChild];
            default:
                return [];
        }
    }

    private static string GetText(TextBlock textBlock)
    {
        if (textBlock.Inlines is { Count: > 0 } inlines)
        {
            return string.Concat(inlines.OfType<Run>().Select(run => run.Text));
        }

        return textBlock.Text ?? string.Empty;
    }
}

