using PuraMarkdown.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class RenderTests
{
    private static PuraMarkdownOptions CreateOptions(bool interactionEnabled = true)
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .Build(),
            InteractionEnabled = interactionEnabled,
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public void Renders_standard_markdown_blocks()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = """
            # Title

            Hello **world** and `code`.

            - one
            - two

            > quote

            ```csharp
            var x = 1;
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("Title", dump);
        Assert.Contains("world", dump);
        Assert.Contains("code", dump);
        Assert.Contains("•", dump);
        Assert.Contains("quote", dump);
        Assert.Contains("csharp", dump);
        Assert.Contains("var x = 1;", dump);
    }

    [AvaloniaFact]
    public void Renders_table()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = """
            | Name | Value |
            | --- | --- |
            | A | 1 |
            """;

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("Name", dump);
        Assert.Contains("Value", dump);
        Assert.Contains("A", dump);
    }

    [AvaloniaFact]
    public void Renders_invalid_agent_payload_as_error_card()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "```pura-agent\n{ not json }\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("JSON", dump);
        Assert.Contains("解析失败", dump);
    }

    [AvaloniaFact]
    public void Disables_interactions_in_read_only_mode()
    {
        var view = new PuraMarkdownView { Options = CreateOptions(interactionEnabled: false) };
        view.Markdown = """
            ```pura-agent
            {"type":"choice","version":"1.0","id":"c1","question":"Q","options":[{"id":"a","label":"A"}]}
            ```
            """;

        var radios = ControlTreeDumper.Find<Avalonia.Controls.RadioButton>(view).ToList();

        Assert.Single(radios);
        Assert.False(radios[0].IsEnabled);
    }
}

