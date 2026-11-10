using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using PuraMarkdown.Agent.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class MarkdownEdgeCaseTests
{
    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            Theme = PuraTheme.Light,
        };

    private static TextBlock FirstParagraph(PuraMarkdownView view)
        => ControlTreeDumper.Find<TextBlock>(view).First(block => block.Inlines is { Count: > 0 });

    private static string TextOf(TextBlock block)
        => block.Text ?? string.Concat(block.Inlines?.OfType<Run>().Select(run => run.Text) ?? []);

    [AvaloniaFact]
    public void Soft_line_break_renders_as_space()
    {
        var view = new PuraMarkdownView { Options = CreateOptions(), Markdown = "line1\nline2" };
        var paragraph = FirstParagraph(view);

        Assert.DoesNotContain(paragraph.Inlines!, inline => inline is LineBreak);
        Assert.Equal("line1 line2", TextOf(paragraph));
    }

    [AvaloniaFact]
    public void Hard_line_break_renders_as_line_break()
    {
        var view = new PuraMarkdownView { Options = CreateOptions(), Markdown = "line1  \nline2" };
        var paragraph = FirstParagraph(view);

        Assert.Contains(paragraph.Inlines!, inline => inline is LineBreak);
    }

    [AvaloniaFact]
    public void Long_unclosed_fence_is_not_split_by_forced_commit()
    {
        var code = string.Join("\n\n", Enumerable.Range(0, 3000).Select(i => $"line {i}"));
        var markdown = "```csharp\n" + code;
        Assert.True(markdown.Length > 16 * 1024);

        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append(markdown);

        Assert.Single(view.Blocks);
    }
}


