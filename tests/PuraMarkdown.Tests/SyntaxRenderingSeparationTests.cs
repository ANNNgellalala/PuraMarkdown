using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Core;
using PuraMarkdown.Agent.Testing;
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
    public void Syntax_incremental_append_reuses_stable_prefix()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var first = syntax.ParseDocument("# A\n\n");
        var firstBlockCount = first.Markdown.Count;

        var next = syntax.ParseIncremental(first, new PuraTextChange(first.Text.Length, 0, "## B\n"));

        Assert.Equal(2, next.Outline.Headings.Count);
        Assert.Equal("B", next.Outline.Headings[1].Text);
        Assert.Equal(firstBlockCount + 1, next.Markdown.Count);
    }

    [Fact]
    public void Syntax_incremental_handles_arbitrary_replace_and_reuses_blocks()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var text = "# A\n\nfirst\n\nsecond\n\nthird\n";
        var first = syntax.ParseDocument(text);
        var prefix = first.Markdown[0];
        var offset = text.IndexOf("second", StringComparison.Ordinal);

        var next = syntax.ParseIncremental(
            first,
            new PuraTextChange(offset, "second".Length, "second-long"));

        Assert.Contains("second-long", next.Text, StringComparison.Ordinal);
        Assert.Equal(4, next.Markdown.Count);
        Assert.Same(prefix, next.Markdown[0]);
        foreach (var block in next.Markdown)
        {
            Assert.InRange(block.Span.Start, 0, next.Text.Length);
            Assert.InRange(block.Span.End, 0, next.Text.Length);
        }
    }

    [Fact]
    public void Syntax_incremental_falls_back_for_reference_definitions()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var text = "[ref]: https://example.com\n\n[x][ref]\n";
        var first = syntax.ParseDocument(text);
        var offset = text.IndexOf("[x]", StringComparison.Ordinal) + 1;

        var next = syntax.ParseIncremental(first, new PuraTextChange(offset, 1, "y"));

        Assert.Contains("[y][ref]", next.Text, StringComparison.Ordinal);
        var link = next.Markdown.Descendants<Markdig.Syntax.Inlines.LinkInline>().Single();
        Assert.Equal("https://example.com", link.Url);
    }

    [Fact]
    public void Syntax_incremental_handles_middle_insert_and_delete()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var text = "# A\n\nfirst\n\nsecond\n\nthird\n";

        var first = syntax.ParseDocument(text);
        var insertAt = text.IndexOf("second", StringComparison.Ordinal);
        var inserted = syntax.ParseIncremental(first, new PuraTextChange(insertAt, 0, "inserted\n\n"));
        Assert.Equal(5, inserted.Markdown.Count);
        Assert.Contains("inserted", inserted.Text, StringComparison.Ordinal);

        var deleted = syntax.ParseIncremental(
            inserted,
            new PuraTextChange(inserted.Text.IndexOf("second", StringComparison.Ordinal), "second\n\n".Length, string.Empty));
        Assert.Equal(4, deleted.Markdown.Count);
        Assert.DoesNotContain("second", deleted.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Syntax_incremental_reparses_fenced_code_context()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var text = "# A\n\n```csharp\nvar x = 1;\n```\n";
        var first = syntax.ParseDocument(text);
        var offset = text.IndexOf("1", StringComparison.Ordinal);

        var next = syntax.ParseIncremental(first, new PuraTextChange(offset, 1, "42"));

        Assert.Contains("var x = 42;", next.Text, StringComparison.Ordinal);
        Assert.Contains(next.Markdown, block => block is Markdig.Syntax.FencedCodeBlock);
    }

    [Fact]
    public void Markdig_version_matches_compatibility_matrix()
    {
        Assert.Equal("1.5.1", MarkdigCompatibility.TestedPackageVersion);
        Assert.Equal(MarkdigCompatibility.TestedAssemblyVersion, MarkdigCompatibility.CurrentAssemblyVersion);
    }

    [Fact]
    public void Comprehensive_document_only_uses_contract_nodes()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var document = syntax.ParseDocument("""
            # H1

            > quote **bold** [link](https://example.com) ![img](https://example.com/a.png)

            - [x] task
            - [ ] todo

            | A | B |
            | --- | ---: |
            | 1 | 2 |

            ```csharp
            var x = 1;
            ```

            https://example.com
            """);

        foreach (var node in document.Markdown.Descendants())
        {
            Assert.True(
                MarkdigCompatibility.IsSupported(node),
                $"节点 {node.GetType().Name} 不在 Markdig 支持矩阵中");
        }

        Assert.Contains(
            MarkdigCompatibility.Nodes,
            info => info.NodeType == typeof(Table) && info.Support == PuraNodeSupport.Rendered);
        Assert.Contains(
            MarkdigCompatibility.Nodes,
            info => info.NodeType == typeof(TaskList) && info.Support == PuraNodeSupport.Rendered);
    }

    [Fact]
    public void Syntax_document_contains_security_diagnostics()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().Build();
        var document = syntax.ParseDocument("[bad](javascript:alert(1))");

        Assert.NotNull(document.Sanitized);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PURA-SEC-001");
        Assert.NotEmpty(document.Sanitized!.Warnings);
    }

    [Fact]
    public void Syntax_document_contains_budget_diagnostics()
    {
        var syntax = new PuraSyntaxBuilder()
            .UseCommonMark()
            .UseGfm()
            .ConfigureLimits(limits => limits.MaxBlocks = 1)
            .Build();
        var document = syntax.ParseDocument("a\n\nb\n\nc");

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "PURA-LIM-005");
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












