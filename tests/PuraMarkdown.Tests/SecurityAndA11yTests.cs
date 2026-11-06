using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using PuraMarkdown.Platform;
using PuraMarkdown.Rendering;
using PuraMarkdown.Security;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class SecurityAndA11yTests
{
    private static PuraMarkdownOptions CreateOptions(
        Action<PuraLimits>? configureLimits = null,
        IUrlPolicy? urlPolicy = null)
    {
        var limits = new PuraLimits();
        configureLimits?.Invoke(limits);
        return new PuraMarkdownOptions
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().UseAgentComponents().Build(),
            Theme = PuraTheme.Light,
            Limits = limits,
            UrlPolicy = urlPolicy ?? DefaultUrlPolicy.Instance,
        };
    }

    [AvaloniaFact]
    public void Raw_html_is_rendered_as_literal_text()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "<script>alert(1)</script> and <b onclick=\"x\">bold</b>";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("script", dump);
        Assert.Contains("onclick", dump);
        Assert.Empty(ControlTreeDumper.Find<Button>(view));
    }

    [AvaloniaFact]
    public void Javascript_links_are_not_clickable()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "[click](javascript:alert(1))";

        Assert.Empty(ControlTreeDumper.Find<HyperlinkButton>(view));
    }

    [AvaloniaFact]
    public void Data_uri_images_are_rejected()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "![alt](data:image/png;base64,AAAA)";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("图片被拒绝", dump);
    }

    [AvaloniaFact]
    public void Oversized_document_is_truncated_with_warning()
    {
        var warnings = new List<PuraWarning>();
        var view = new PuraMarkdownView { Options = CreateOptions(limits => limits.MaxDocumentChars = 100) };
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = new string('a', 5000);

        Assert.Contains(warnings, warning => warning.Code == "PURA-LIM-001");
    }

    [AvaloniaFact]
    public void Deeply_nested_input_does_not_crash()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = string.Concat(Enumerable.Repeat("> ", 200)) + "deep";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("deep", dump);
    }

    [AvaloniaFact]
    public void Heading_has_automation_level()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "## Heading";

        var heading = ControlTreeDumper.Find<TextBlock>(view).Single(block => AutomationProperties.GetHeadingLevel(block) == 2);

        Assert.Equal(2, AutomationProperties.GetHeadingLevel(heading));
    }

    [AvaloniaFact]
    public void View_announces_through_live_setting()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        string? announced = null;
        view.Announced += (_, message) => announced = message;

        view.Announce("done");

        Assert.Equal("done", announced);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(view));
    }

    [Fact]
    public void Theme_contrast_meets_wcag_aa()
    {
        AssertContrast(PuraTheme.Light);
        AssertContrast(PuraTheme.Dark);
    }

    private static void AssertContrast(PuraTheme theme)
    {
        var ratio = ContrastRatio(theme.Colors.Text, theme.Colors.Background);
        Assert.True(ratio >= 4.5, $"{theme.Name} 文本/背景对比度不足：{ratio:F2}");
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var light = Math.Max(Luminance(foreground), Luminance(background));
        var dark = Math.Min(Luminance(foreground), Luminance(background));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}



