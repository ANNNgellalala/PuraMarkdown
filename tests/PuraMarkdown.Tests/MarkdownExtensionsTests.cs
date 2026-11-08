using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Markdig.Syntax;
using PuraMarkdown.Rendering;
using PuraMarkdown.Syntax;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class MarkdownExtensionsTests
{
    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseFootnotes()
                .UseDefinitionLists()
                .UseAlertBlocks()
                .Build(),
            Theme = PuraTheme.Light,
        };

    [Fact]
    public void Extension_syntax_nodes_are_in_compatibility_matrix()
    {
        var syntax = new PuraSyntaxBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseFootnotes()
            .UseDefinitionLists()
            .UseAlertBlocks()
            .Build();
        var document = syntax.ParseDocument("""
            Text[^1]

            [^1]: Note

            Term
            : Definition

            > [!NOTE]
            > Body
            """);

        foreach (var node in document.Markdown.Descendants())
        {
            Assert.True(MarkdigCompatibility.IsSupported(node), node.GetType().Name);
        }

        Assert.Equal(
            PuraNodeSupport.Rendered,
            MarkdigCompatibility.Describe(document.Markdown.Descendants<Markdig.Extensions.Alerts.AlertBlock>().Single())!.Support);
        Assert.Equal(
            PuraNodeSupport.Rendered,
            MarkdigCompatibility.Describe(document.Markdown.Descendants<Markdig.Extensions.Footnotes.FootnoteGroup>().Single())!.Support);
    }

    [AvaloniaFact]
    public void Footnotes_render_reference_group_and_backlink()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "Text[^1]\n\n[^1]: Note **bold**\n",
        };

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("Note", dump, StringComparison.Ordinal);
        Assert.Contains("↩", dump, StringComparison.Ordinal);
        Assert.True(ControlTreeDumper.Find<HyperlinkButton>(view).Count() >= 2);
    }

    [AvaloniaFact]
    public void Definition_list_renders_term_and_definition()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "Term\n: Definition **bold**\n",
        };

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("Term", dump, StringComparison.Ordinal);
        Assert.Contains("Definition", dump, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void GitHub_alert_renders_localized_title_and_body()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "> [!WARNING]\n> Be careful\n",
        };

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("警告", dump, StringComparison.Ordinal);
        Assert.Contains("Be careful", dump, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Emphasis_extras_render_semantics()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "~sub~ ^sup^ ++ins++ ==mark== ~~strike~~",
        };

        var runs = ControlTreeDumper.Find<TextBlock>(view)
            .SelectMany(block => block.Inlines?.OfType<Run>() ?? [])
            .ToArray();

        Assert.Contains(runs, run => run.BaselineAlignment == BaselineAlignment.Subscript);
        Assert.Contains(runs, run => run.BaselineAlignment == BaselineAlignment.Superscript);
        Assert.Contains(runs, run => run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true);
        Assert.Contains(runs, run => run.Background is not null);
        Assert.Contains(runs, run => run.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true);
    }
}




