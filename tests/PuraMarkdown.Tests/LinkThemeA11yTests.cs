using PuraMarkdown.Testing;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using PuraMarkdown.Platform;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class LinkThemeA11yTests
{
    private static PuraMarkdownOptions CreateOptions(
        ILinkHandler? linkHandler = null,
        PuraTheme? theme = null)
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .Build(),
            Theme = theme ?? PuraTheme.Light,
            LinkHandler = linkHandler ?? NullLinkHandler.Instance,
        };

    [AvaloniaFact]
    public void Link_is_clickable_and_routes_to_handler()
    {
        var handler = new CapturingLinkHandler();
        var view = new PuraMarkdownView { Options = CreateOptions(handler) };
        view.Markdown = "See [docs](https://example.com).";

        var hyperlink = ControlTreeDumper.Find<HyperlinkButton>(view).Single();
        hyperlink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("https://example.com", handler.Url);
    }

    [AvaloniaFact]
    public void Dangerous_link_is_not_clickable()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "[x](javascript:alert(1))";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Empty(ControlTreeDumper.Find<HyperlinkButton>(view));
        Assert.Contains("x", dump);
    }

    [AvaloniaFact]
    public void Task_list_renders_readonly_checkbox()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "- [x] done\n- [ ] todo";

        var checkBoxes = ControlTreeDumper.Find<CheckBox>(view).ToList();

        Assert.Equal(2, checkBoxes.Count);
        Assert.True(checkBoxes[0].IsChecked);
        Assert.False(checkBoxes[1].IsChecked);
        Assert.All(checkBoxes, box => Assert.False(box.IsEnabled));
    }

    [AvaloniaFact]
    public void Choice_options_have_automation_names()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = """
            ```pura-agent
            {"type":"choice","version":"1.0","question":"选择环境","options":[{"id":"a","label":"开发"},{"id":"b","label":"生产"}]}
            ```
            """;

        var radios = ControlTreeDumper.Find<RadioButton>(view).ToList();

        Assert.Equal(["开发", "生产"], radios.Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public void Theme_tokens_are_applied()
    {
        var view = new PuraMarkdownView { Options = CreateOptions(theme: PuraTheme.Dark) };
        view.Markdown = "hello";

        var textBlock = ControlTreeDumper.Find<TextBlock>(view).First();
        var brush = Assert.IsType<SolidColorBrush>(textBlock.Foreground);

        Assert.Equal(PuraTheme.Dark.Colors.Text, brush.Color);
    }

    private sealed class CapturingLinkHandler : ILinkHandler
    {
        public string? Url { get; private set; }

        public Task OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            Url = url;
            return Task.CompletedTask;
        }
    }
}

