using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using PuraMarkdown.Localization;

namespace PuraMarkdown.Rendering;

/// <summary>
/// 数学公式的默认文本回退：核心不依赖任何 TeX 排版引擎，只显示 TeX 源码。
/// 需要真正排版时，由宿主/插件通过 <c>UseInlineRenderer</c>/<c>UseRenderer</c> 接管
/// <see cref="MathInline"/>/<see cref="MathBlock"/>（示例见 samples/PuraMarkdown.Plugins.Samples）。
/// </summary>
internal static class PuraMathRenderer
{
    /// <summary>行内数学文本回退。</summary>
    public static void AppendInline(string latex, InlineCollection target, RenderContext context)
        => target.Add(CreateFallbackRun(latex, context));

    /// <summary>块级数学文本回退（带复制 TeX 按钮）。</summary>
    public static Control CreateBlock(string latex, RenderContext context)
    {
        var theme = context.Theme;
        var localizer = context.Options.Localizer;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "math",
            FontSize = theme.Typography.BodySize - 2,
            Foreground = new SolidColorBrush(theme.Colors.Muted),
            FontFamily = theme.Typography.MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var copyButton = new Button
        {
            Content = localizer.Get(PuraStringKeys.Copy),
            FontSize = theme.Typography.BodySize - 3,
            Padding = new Thickness(8, 2),
        };
        copyButton.Click += async (_, _) =>
        {
            try
            {
                await context.Options.Clipboard.SetTextAsync(latex);
                copyButton.Content = localizer.Get(PuraStringKeys.Copied);
            }
            catch
            {
                copyButton.Content = localizer.Get(PuraStringKeys.CopyFailed);
            }
        };
        AutomationProperties.SetName(copyButton, localizer.Get(PuraStringKeys.CopyMathAutomation));
        Grid.SetColumn(copyButton, 1);
        header.Children.Add(copyButton);

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = latex,
            FontFamily = theme.Typography.MonoFont,
            FontSize = theme.Typography.BodySize - 1,
            Foreground = new SolidColorBrush(theme.Colors.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            BorderBrush = new SolidColorBrush(theme.Colors.CodeBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(theme.Spacing.CodePadding),
            Margin = new Thickness(0, 0, 0, theme.Spacing.CodeBlockSpacing),
            Child = stack,
        };
    }

    private static Run CreateFallbackRun(string latex, RenderContext context) => new($"${latex}$")
    {
        FontFamily = context.Theme.Typography.MonoFont,
        FontSize = context.Theme.Typography.BodySize - 1,
        Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
    };
}

/// <summary>块级数学默认渲染器（TeX 文本回退；优先级低于插件注册的 MathBlock 渲染器）。</summary>
internal sealed class MathBlockRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is MathBlock;

    /// <summary>Render。</summary>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var latex = MarkdownText.GetLeafText((MathBlock)node).TrimEnd('\n').Trim();
        return PuraMathRenderer.CreateBlock(latex, context);
    }
}
