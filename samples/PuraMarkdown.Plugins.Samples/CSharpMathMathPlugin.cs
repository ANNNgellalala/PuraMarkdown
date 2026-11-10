using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CSharpMath.Avalonia;
using CSharpMath.Rendering.FrontEnd;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using PuraMarkdown.Localization;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Plugins.Samples;

/// <summary>
/// 数学公式渲染示例插件：核心只提供 Markdig 语法与 TeX 文本回退，
/// 该示例展示如何用 CSharpMath 接管 <see cref="MathInline"/>/<see cref="MathBlock"/>。
/// CSharpMath 依赖只在示例/Demo 中引用，不进核心包。
/// </summary>
public sealed class CSharpMathMathPlugin : IPuraPlugin
{
    /// <summary>Name。</summary>
    public string Name => "pura.samples.math.csharpmath";

    /// <summary>Version。</summary>
    public string Version => "1.0.0";

    /// <summary>Description。</summary>
    public string? Description => "CSharpMath 数学公式示例渲染器（核心零引擎依赖）";

    /// <summary>注册行内与块级数学渲染器。</summary>
    public void Configure(PuraPipelineBuilder builder)
    {
        builder.UseInlineRenderer(new CSharpMathMathInlineRenderer());
        builder.UseRenderer(new CSharpMathMathBlockRenderer());
    }
}

/// <summary>行内数学示例：<see cref="MathInline"/> → CSharpMath <see cref="MathView"/>。</summary>
public sealed class CSharpMathMathInlineRenderer : IPuraInlineRenderer
{
    /// <summary>CanRender。</summary>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is MathInline;

    /// <summary>RenderInline。</summary>
    public Inline RenderInline(MarkdownObject node, RenderContext context)
    {
        var latex = ((MathInline)node).Content.ToString();
        if (context.IsTail)
        {
            return new Run($"${latex}$")
            {
                FontFamily = context.Theme.Typography.MonoFont,
                FontSize = context.Theme.Typography.BodySize - 1,
                Foreground = new SolidColorBrush(context.Theme.Colors.Muted),
            };
        }

        return new InlineUIContainer(CreateMathView(latex, context, display: false))
        {
            BaselineAlignment = BaselineAlignment.Baseline,
        };
    }

    /// <summary>块级渲染入口未使用；返回文本回退以保持接口契约。</summary>
    public Control Render(MarkdownObject node, RenderContext context)
        => new TextBlock
        {
            Text = ((MathInline)node).Content.ToString(),
            FontFamily = context.Theme.Typography.MonoFont,
        };

    internal static MathView CreateMathView(string latex, RenderContext context, bool display)
    {
        var theme = context.Theme;
        var view = new MathView
        {
            LaTeX = latex,
            // CSharpMath 的 FontSize 单位是 point；Avalonia 是 DIP（1 DIP = 0.75pt）。
            FontSize = (float)(theme.Typography.BodySize * (display ? 0.95 : 0.85)),
            TextColor = theme.Colors.Text,
            PaintStyle = PaintStyle.Fill,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(1, 0, 1, 0),
            HorizontalAlignment = display ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(view, latex);
        return view;
    }
}

/// <summary>块级数学示例：<see cref="MathBlock"/> → CSharpMath <see cref="MathView"/>。</summary>
public sealed class CSharpMathMathBlockRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is MathBlock;

    /// <summary>Render。</summary>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var latex = GetMathText((MathBlock)node);
        var theme = context.Theme;
        var localizer = context.Options.Localizer;

        if (context.IsStreaming && context.IsTail)
        {
            return CreateContainer(context, new TextBlock
            {
                Text = latex,
                FontFamily = theme.Typography.MonoFont,
                FontSize = theme.Typography.BodySize - 1,
                Foreground = new SolidColorBrush(theme.Colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
        }

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
            Padding = new Avalonia.Thickness(8, 2),
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

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = CSharpMathMathInlineRenderer.CreateMathView(latex, context, display: true),
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(header);
        stack.Children.Add(body);
        return CreateContainer(context, stack);
    }

    private static Control CreateContainer(RenderContext context, Control child)
    {
        var theme = context.Theme;
        return new Border
        {
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            BorderBrush = new SolidColorBrush(theme.Colors.CodeBorder),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Avalonia.Thickness(theme.Spacing.CodePadding),
            Margin = new Avalonia.Thickness(0, 0, 0, theme.Spacing.CodeBlockSpacing),
            Child = child,
        };
    }

    private static string GetMathText(LeafBlock block)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
        {
            builder.Append(block.Lines.Lines[i].Slice.ToString());
            builder.Append('\n');
        }

        return builder.ToString().TrimEnd('\n').Trim();
    }
}
