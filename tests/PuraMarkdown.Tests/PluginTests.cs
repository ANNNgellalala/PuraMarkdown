using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Markdig.Syntax;
using PuraMarkdown.Agent;
using PuraMarkdown.Highlighting.ColorCode;
using PluginSamples = PuraMarkdown.Plugins.Samples;
using PuraMarkdown.Rendering;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class PluginTests
{
    [Fact]
    public void Duplicate_plugin_name_is_rejected()
    {
        var builder = new PuraPipelineBuilder().UsePlugin(new NamedPlugin("pura.test"));

        var exception = Assert.Throws<PuraConfigurationException>(() => builder.UsePlugin(new NamedPlugin("pura.test")));
        Assert.Contains("重复注册", exception.Message);
    }

    [Fact]
    public void Blank_plugin_name_is_rejected()
    {
        var builder = new PuraPipelineBuilder();

        Assert.Throws<PuraConfigurationException>(() => builder.UsePlugin(new NamedPlugin(" ")));
    }

    [Fact]
    public void Duplicate_fence_renderer_is_rejected()
    {
        var builder = new PuraPipelineBuilder()
            .UseFenceRenderer(new PluginSamples.SampleDiagramRenderer())
            .UseFenceRenderer(new PluginSamples.SampleDiagramRenderer());

        Assert.Throws<PuraConfigurationException>(() => builder.Build());
    }

    [AvaloniaFact]
    public void Plugin_renderer_overrides_default()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new OverrideParagraphPlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Markdown = "hello";

        Assert.Contains("plugin-paragraph", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Throwing_renderer_falls_back_and_warns()
    {
        var warnings = new List<PuraWarning>();
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new ThrowingParagraphPlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Warning += (_, args) => warnings.Add(args.Warning);
        view.Markdown = "hello";

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("hello", dump);
        Assert.Contains(warnings, warning => warning.Code == "PURA-RENDER-010");
    }

    [AvaloniaFact]
    public void ColorCode_plugin_highlights_code()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new ColorCodePlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Markdown = "```csharp\npublic void M() {}\n```";

        var codeText = ControlTreeDumper.Find<TextBlock>(view)
            .Single(block => GetText(block) == "public void M() {}");

        Assert.NotNull(codeText.Inlines);
        var colored = codeText.Inlines!.OfType<Run>()
            .Any(run => run.Foreground is SolidColorBrush brush && brush.Color != PuraTheme.Light.Colors.Text);
        Assert.True(colored, "应至少有一个 token 被着色");
    }

    [AvaloniaFact]
    public void Agent_components_can_be_composed()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseAgentComponents()
            .UseAgentComponent(new PluginSamples.RatingParser(), new PluginSamples.RatingRenderer())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Markdown = """
            ```pura-agent
            {"type":"choice","version":"1.0","question":"Q","options":[{"id":"a","label":"A"}]}
            ```

            ```pura-agent
            { "type": "rating", "version": "1.0", "label": "评分", "max": 5 }
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);
        Assert.Single(ControlTreeDumper.Find<Avalonia.Controls.RadioButton>(view));
        Assert.Contains(ControlTreeDumper.Find<Avalonia.Controls.Button>(view), button => Equals(button.Content, "5"));
    }

    [AvaloniaFact]
    public void Rating_plugin_renders_and_reports()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new PluginSamples.RatingPlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            { "type": "rating", "version": "1.0", "id": "r1", "label": "质量", "max": 5, "value": 3 }
            ```
            """;

        var buttons = ControlTreeDumper.Find<Button>(view)
            .Where(button => button.Content is string)
            .ToList();
        Assert.Equal(5, buttons.Count);

        InteractionProbe.Click(buttons[4]);

        Assert.NotNull(captured);
        Assert.Equal("rating", captured!.ComponentType);
        Assert.Equal("r1", captured.ComponentId);
        Assert.Equal(["5"], captured.SelectedIds);
    }

    [AvaloniaFact]
    public void Diagram_plugin_renders_fence()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new PluginSamples.SampleDiagramPlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Markdown = "```mermaid\ngraph TD\n  A --> B\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("Mermaid（示例插件渲染）", dump);
    }

    [AvaloniaFact]
    public void Diagram_fence_falls_back_to_code_without_plugin()
    {
        var pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build();
        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Markdown = "```mermaid\ngraph TD\n  A --> B\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("graph TD", dump);
        Assert.DoesNotContain("Mermaid（示例插件渲染）", dump);
    }

    [AvaloniaFact]
    public void Throwing_diagram_falls_back_to_code_and_warns()
    {
        var warnings = new List<PuraWarning>();
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UsePlugin(new ThrowingDiagramPlugin())
            .Build();

        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Warning += (_, args) => warnings.Add(args.Warning);
        view.Markdown = "```mermaid\ngraph TD\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("graph TD", dump);
        Assert.Contains(warnings, warning => warning.Code == "PURA-RENDER-003");
    }

    [AvaloniaFact]
    public async Task Streaming_driver_helper_completes()
    {
        var pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build();
        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };

        await StreamingDriver.AppendInChunksAsync(view, "# Hello\n\nworld", chunkSize: 3);

        Assert.Contains("Hello", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Official_plugins_pass_conformance()
    {
        var colorCode = PluginConformance.VerifyRegistration(new ColorCodePlugin());
        Assert.Contains("pura.highlighting.colorcode", colorCode.Environment.PluginNames);

        var rating = PluginConformance.VerifyRegistration(new PluginSamples.RatingPlugin());
        Assert.Contains("acme.rating", rating.Environment.PluginNames);

        var diagram = PluginConformance.VerifyRegistration(new PluginSamples.SampleDiagramPlugin());
        Assert.Contains("pura.samples.diagram", diagram.Environment.PluginNames);
    }

    private static string GetText(TextBlock block)
        => block.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
            : block.Text ?? string.Empty;

    private sealed class NamedPlugin(string name) : IPuraPlugin
    {
        public string Name => name;

        public string Version => "1.0.0";

        public string? Description => null;

        public void Configure(PuraPipelineBuilder builder)
        {
        }
    }

    private sealed class OverrideParagraphPlugin : IPuraPlugin
    {
        public string Name => "pura.test.override";

        public string Version => "1.0.0";

        public string? Description => null;

        public void Configure(PuraPipelineBuilder builder)
            => builder.UseRenderer(new PluginParagraphRenderer(), priority: 300);
    }

    private sealed class ThrowingParagraphPlugin : IPuraPlugin
    {
        public string Name => "pura.test.throwing";

        public string Version => "1.0.0";

        public string? Description => null;

        public void Configure(PuraPipelineBuilder builder)
            => builder.UseRenderer(new ThrowingParagraphRenderer(), priority: 500);
    }

    private sealed class ThrowingDiagramPlugin : IPuraPlugin
    {
        public string Name => "pura.test.throwing-diagram";

        public string Version => "1.0.0";

        public string? Description => null;

        public void Configure(PuraPipelineBuilder builder)
            => builder.UseFenceRenderer(new ThrowingDiagramRenderer());
    }

    private sealed class PluginParagraphRenderer : IPuraRenderer
    {
        public bool CanRender(MarkdownObject node, RenderContext context) => node is ParagraphBlock;

        public Control Render(MarkdownObject node, RenderContext context)
            => new TextBlock { Text = "plugin-paragraph" };
    }

    private sealed class ThrowingParagraphRenderer : IPuraRenderer
    {
        public bool CanRender(MarkdownObject node, RenderContext context) => node is ParagraphBlock;

        public Control Render(MarkdownObject node, RenderContext context)
            => throw new InvalidOperationException("boom");
    }

    private sealed class ThrowingDiagramRenderer : IFenceRenderer
    {
        public string Name => "pura.test.throwing-diagram";

        public bool CanRender(string language, string source, bool isClosed, RenderContext context)
            => string.Equals(language, "mermaid", StringComparison.OrdinalIgnoreCase);

        public Control Render(string language, string source, bool isClosed, RenderContext context)
            => throw new InvalidOperationException("diagram boom");
    }
}






