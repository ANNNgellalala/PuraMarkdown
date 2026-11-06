using PuraMarkdown.Testing;
using Markdig.Syntax;
using PuraMarkdown.Agent;
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

    [Fact]
    public void Pura_agent_fence_becomes_agent_block()
    {
        var pipeline = CreatePipeline();
        var document = pipeline.Parse("""
            ```pura-agent
            {"type":"choice","version":"1.0","question":"Q","options":[{"id":"a","label":"A"}]}
            ```
            """);

        var block = document.OfType<PuraAgentBlock>().Single();

        Assert.True(block.IsClosed);
        Assert.Contains("pura-agent", block.Info, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unclosed_agent_fence_is_marked_incomplete()
    {
        var pipeline = CreatePipeline();
        var document = pipeline.Parse("```pura-agent\n{\"type\":\"choice\"");

        var block = document.OfType<PuraAgentBlock>().Single();

        Assert.False(block.IsClosed);
    }

    [Fact]
    public void Other_fences_stay_as_standard_code_blocks()
    {
        var pipeline = CreatePipeline();
        var document = pipeline.Parse("```csharp\nvar x = 1;\n```");

        Assert.Empty(document.OfType<PuraAgentBlock>());
        Assert.NotEmpty(document.OfType<FencedCodeBlock>());
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Unclosed_agent_fence_renders_loading_placeholder()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = CreatePipeline() },
        };

        view.Markdown = "```pura-agent\n{\"type\":\"choice\"";

        Assert.Contains("组件加载中", ControlTreeDumper.Dump(view));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
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


