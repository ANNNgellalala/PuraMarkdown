using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PuraMarkdown.Agent;

/// <summary>组件解析失败/未知类型时的降级卡：显示原因 + 原始 JSON。</summary>
internal static class AgentCards
{
    /// <summary>Error。</summary>
    /// <param name="theme">theme 参数。</param>
    /// <param name="title">title 参数。</param>
    /// <param name="detail">detail 参数。</param>
    /// <param name="raw">raw 参数。</param>
    public static Control Error(PuraTheme theme, string title, string? detail, string raw)
        => Build(theme, title, detail, raw, theme.Colors.Danger);

    /// <summary>Unsupported。</summary>
    /// <param name="theme">theme 参数。</param>
    /// <param name="title">title 参数。</param>
    /// <param name="type">type 参数。</param>
    /// <param name="raw">raw 参数。</param>
    public static Control Unsupported(PuraTheme theme, string title, string type, string raw)
        => Build(theme, title, type, raw, theme.Colors.Muted);

    /// <summary>Loading。</summary>
    /// <param name="theme">theme 参数。</param>
    /// <param name="text">text 参数。</param>
    public static Control Loading(PuraTheme theme, string text)
    {
        var colors = theme.Colors;
        return new Border
        {
            BorderBrush = new SolidColorBrush(colors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(colors.CodeBackground),
            Child = new TextBlock
            {
                Text = text,
                FontSize = theme.Typography.BodySize - 1,
                Foreground = new SolidColorBrush(colors.Muted),
            },
        };
    }

    private static Control Build(PuraTheme theme, string title, string? detail, string raw, Color accent)
    {
        var colors = theme.Colors;
        var typography = theme.Typography;

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(accent),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = typography.BodySize - 1,
                Foreground = new SolidColorBrush(colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrWhiteSpace(raw))
        {
            stack.Children.Add(new ScrollViewer
            {
                MaxHeight = 200,
                Content = new TextBlock
                {
                    Text = raw,
                    FontFamily = typography.MonoFont,
                    FontSize = typography.BodySize - 2,
                    Foreground = new SolidColorBrush(colors.Text),
                    TextWrapping = TextWrapping.NoWrap,
                },
            });
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(colors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(colors.CodeBackground),
            Child = stack,
        };
    }
}

