using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Markdig.Syntax;
using PuraMarkdown.Rendering;
using PuraMarkdown.Syntax;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class SyntaxRenderingSeparationTests
{
    private static PuraMarkdownOptions CreateOptions(params IPuraRenderer[] renderers)
    {
        var builder = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm();
        foreach (var renderer in renderers)
        {
            builder.UseRenderer(renderer, priority: 100);
        }

        return new PuraMarkdownOptions
        {
            Pipeline = builder.Build(),
            Theme = PuraTheme.Light,
        };
    }

    [Fact]
    public void Syntax_pipeline_produces_outline_without_avalonia()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var document = syntax.ParseDocument("# Title\n\n## Sub\n\ntext", version: 3);

        Assert.Equal(3, document.Version);
        Assert.Equal(2, document.Outline.Headings.Count);
        Assert.Equal("title", document.Outline.Headings[0].Anchor);
        Assert.IsType<HeadingBlock>(document.Markdown[0]);
    }

    [Fact]
    public void Syntax_streaming_session_updates_outline()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var session = new PuraSyntaxStreamingSession(syntax);

        session.Append("# First");
        Assert.Single(session.Current!.Outline.Headings);

        session.Append("\n\n## Second");
        var document = session.Complete();
        Assert.Equal(2, document.Outline.Headings.Count);
        Assert.Equal("second", document.Outline.Headings[1].Anchor);
    }

    [AvaloniaFact]
    public void Document_view_renders_preparsed_document()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var document = syntax.ParseDocument("# Title\n\nbody");
        var view = new PuraDocumentView
        {
            Options = CreateOptions(),
            Document = document,
        };

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("Title", dump);
        Assert.Contains("body", dump);
    }

    [AvaloniaFact]
    public void Arc_style_renderer_can_override_nodes()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var document = syntax.ParseDocument("# Title\n\nbody");
        var view = new PuraDocumentView
        {
            Options = CreateOptions(new ArcHeadingRenderer()),
            Document = document,
        };

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("ARC", dump);
        Assert.Contains("body", dump);
    }

    private sealed class ArcHeadingRenderer : IPuraRenderer
    {
        public bool CanRender(MarkdownObject node, RenderContext context) => node is HeadingBlock;

        public Control Render(MarkdownObject node, RenderContext context)
            => new TextBlock { Text = "ARC" };
    }
}




