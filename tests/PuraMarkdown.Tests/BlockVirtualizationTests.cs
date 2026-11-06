using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class BlockVirtualizationTests
{
    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            Theme = PuraTheme.Light,
            EnableBlockVirtualization = true,
        };

    private static string TextOf(TextBlock block)
        => block.Text ?? string.Concat(block.Inlines?.OfType<Run>().Select(run => run.Text) ?? []);

    [AvaloniaFact]
    public void Virtualized_view_realizes_only_visible_blocks()
    {
        var text = string.Concat(Enumerable.Range(0, 500).Select(i => $"paragraph {i} with **bold** text.\n\n"));
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = text;

        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var realized = window.GetVisualDescendants().OfType<TextBlock>()
            .Count(block => TextOf(block).StartsWith("paragraph", StringComparison.Ordinal));

        Assert.Equal(500, view.Blocks.Count);
        var repeaters = view.GetVisualDescendants().OfType<ItemsRepeater>().Count();
        var allText = view.GetVisualDescendants().OfType<TextBlock>().Count();
        Assert.InRange(realized, 1, 100);
        window.Close();
    }

    [AvaloniaFact]
    public void Virtualized_streaming_keeps_block_metadata_and_limits_controls()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();

        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        var text = string.Concat(Enumerable.Range(0, 200).Select(i => $"streamed paragraph {i}.\n\n"));
        for (var i = 0; i < text.Length; i += 32)
        {
            session.Append(text.Substring(i, Math.Min(32, text.Length - i)));
        }

        Dispatcher.UIThread.RunJobs();

        var realized = window.GetVisualDescendants().OfType<TextBlock>()
            .Count(block => TextOf(block).StartsWith("streamed paragraph", StringComparison.Ordinal));

        Assert.True(view.Blocks.Count >= 200, $"blocks={view.Blocks.Count}");
        Assert.InRange(realized, 0, 100);
        window.Close();
    }
}


