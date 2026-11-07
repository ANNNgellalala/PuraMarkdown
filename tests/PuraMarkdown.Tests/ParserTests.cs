using Avalonia.Headless.XUnit;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class ParserTests
{
    private static PuraPipeline CreatePipeline()
        => new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseAgentComponents()
            .Build();

    [AvaloniaFact]
    public void Agent_fence_renders_choice_component()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = CreatePipeline() },
        };

        view.Markdown = """
            ```pura-agent
            {"type":"choice","version":"1.0","id":"c1","question":"Q","options":[{"id":"a","label":"A"}]}
            ```
            """;

        Assert.Single(ControlTreeDumper.Find<Avalonia.Controls.RadioButton>(view));
    }

    [AvaloniaFact]
    public void Unclosed_agent_fence_renders_loading_placeholder()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = CreatePipeline() },
        };

        view.Markdown = "```pura-agent\n{\"type\":\"choice\"";

        Assert.Contains("交互组件准备中", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Other_fences_stay_as_standard_code_blocks()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = CreatePipeline() },
        };

        view.Markdown = "```csharp\nvar x = 1;\n```";

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("var x = 1", dump);
        Assert.DoesNotContain("交互组件", dump);
    }

    [AvaloniaFact]
    public void Invalid_choice_payload_reports_path()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = CreatePipeline() },
        };

        view.Markdown = """
            ```pura-agent
            {"type":"choice","version":"1.0","question":"Q","options":[{"id":"a"}]}
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("校验失败", dump);
        Assert.Contains("options[0].label", dump);
    }
}
