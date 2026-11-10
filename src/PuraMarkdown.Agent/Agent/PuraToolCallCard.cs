using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Agent.Platform;

namespace PuraMarkdown.Agent;

/// <summary>
/// 工具调用卡片：状态指示 + 工具名 + 状态标签 + 可选耗时，
/// 参数/结果块为限高可折叠区域并带复制按钮；失败时显示错误文本。
/// </summary>
internal sealed class PuraToolCallCard : Border
{
    private const double SectionMaxHeight = 160;

    /// <summary>创建工具调用卡片。</summary>
    /// <param name="call">工具调用模型。</param>
    /// <param name="theme">主题 token。</param>
    /// <param name="localizer">本地化文案。</param>
    /// <param name="clipboard">剪贴板（复制参数/结果）。</param>
    public PuraToolCallCard(
        PuraToolCall call,
        PuraTheme theme,
        IPuraLocalizer localizer,
        IPuraClipboard clipboard)
    {
        var colors = theme.Colors;
        var typography = theme.Typography;
        var statusColor = StatusColor(call.Status, colors);
        var statusText = localizer.Get(PuraToolCall.StatusKey(call.Status));
        var name = string.IsNullOrWhiteSpace(call.Name) ? localizer.Get(PuraStringKeys.ToolCallPending) : call.Name;

        BorderBrush = new SolidColorBrush(colors.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12);
        Background = new SolidColorBrush(colors.CodeBackground);
        AutomationProperties.SetName(this, localizer.Get(PuraStringKeys.ToolCallAutomation, name));

        var root = new StackPanel { Spacing = 6 };
        Child = root;

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(indicator, 0);
        header.Children.Add(indicator);

        var nameText = new TextBlock
        {
            Text = name,
            FontFamily = typography.MonoFont,
            FontWeight = FontWeight.SemiBold,
            FontSize = typography.BodySize,
            Foreground = new SolidColorBrush(colors.Text),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(nameText, localizer.Get(PuraStringKeys.ToolCallAutomation, name));
        Grid.SetColumn(nameText, 1);
        header.Children.Add(nameText);

        var statusLabel = new TextBlock
        {
            Text = statusText,
            FontSize = typography.BodySize - 1,
            Foreground = new SolidColorBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(statusLabel, 2);
        header.Children.Add(statusLabel);

        if (call.Duration is { } duration)
        {
            var durationText = new TextBlock
            {
                Text = PuraToolCallDurationText.Format(duration, localizer),
                FontSize = typography.BodySize - 2,
                Foreground = new SolidColorBrush(colors.Muted),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(durationText, 3);
            header.Children.Add(durationText);
        }

        root.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(call.Error))
        {
            root.Children.Add(new TextBlock
            {
                Text = $"{localizer.Get(PuraStringKeys.ToolCallError)}：{call.Error}",
                FontSize = typography.BodySize - 1,
                Foreground = new SolidColorBrush(colors.Danger),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrEmpty(call.Arguments))
        {
            root.Children.Add(BuildSection(
                localizer.Get(PuraStringKeys.ToolCallArguments),
                call.Arguments,
                localizer.Get(PuraStringKeys.ToolCallCopyArguments),
                theme,
                localizer,
                clipboard));
        }

        if (!string.IsNullOrEmpty(call.Result))
        {
            root.Children.Add(BuildSection(
                localizer.Get(PuraStringKeys.ToolCallResult),
                call.Result,
                localizer.Get(PuraStringKeys.ToolCallCopyResult),
                theme,
                localizer,
                clipboard));
        }
    }

    private static Color StatusColor(PuraToolCallStatus status, PuraColors colors)
        => status switch
        {
            PuraToolCallStatus.Succeeded => colors.Accent,
            PuraToolCallStatus.Failed => colors.Danger,
            _ => colors.Muted,
        };

    private static Control BuildSection(
        string label,
        string text,
        string copyAutomationName,
        PuraTheme theme,
        IPuraLocalizer localizer,
        IPuraClipboard clipboard)
    {
        var colors = theme.Colors;
        var typography = theme.Typography;

        var content = new ScrollViewer
        {
            MaxHeight = SectionMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = text,
                FontFamily = typography.MonoFont,
                FontSize = typography.BodySize - 2,
                Foreground = new SolidColorBrush(colors.Text),
                TextWrapping = TextWrapping.NoWrap,
            },
        };

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = typography.BodySize - 2,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(colors.Muted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(labelText, label);

        var toggle = new Avalonia.Controls.Primitives.ToggleButton
        {
            Content = labelText,
            IsChecked = true,
            Padding = new Thickness(6, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(toggle, label);
        toggle.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty)
            {
                content.IsVisible = toggle.IsChecked == true;
            }
        };

        var copyButton = new Button
        {
            Content = localizer.Get(PuraStringKeys.Copy),
            FontSize = typography.BodySize - 3,
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(copyButton, copyAutomationName);
        copyButton.Click += async (_, _) =>
        {
            try
            {
                await clipboard.SetTextAsync(text);
                copyButton.Content = localizer.Get(PuraStringKeys.Copied);
            }
            catch
            {
                copyButton.Content = localizer.Get(PuraStringKeys.CopyFailed);
            }
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        header.Children.Add(toggle);
        header.Children.Add(copyButton);

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(header);
        section.Children.Add(content);
        return section;
    }
}

/// <summary>工具调用围栏的降级卡（载荷无效时按普通代码块显示）。</summary>
internal static class ToolCallCards
{
    /// <summary>渲染等宽代码块样式的降级内容。</summary>
    /// <param name="theme">主题 token。</param>
    /// <param name="source">原始载荷（调用方负责截断）。</param>
    public static Control CodeFallback(PuraTheme theme, string source)
    {
        var colors = theme.Colors;
        var typography = theme.Typography;
        return new Border
        {
            Background = new SolidColorBrush(colors.CodeBackground),
            BorderBrush = new SolidColorBrush(colors.CodeBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(theme.Spacing.CodePadding),
            Margin = new Thickness(0, 0, 0, theme.Spacing.CodeBlockSpacing),
            Child = new ScrollViewer
            {
                MaxHeight = 240,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = source,
                    FontFamily = typography.MonoFont,
                    FontSize = typography.BodySize - 1,
                    Foreground = new SolidColorBrush(colors.Text),
                    TextWrapping = TextWrapping.NoWrap,
                },
            },
        };
    }
}
