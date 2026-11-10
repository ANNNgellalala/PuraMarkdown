using PuraMarkdown.Agent.Testing;
using Avalonia.Headless.XUnit;
using PuraMarkdown.Agent;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class StreamingTests
{
    private const string Document = """
        # Streaming

        Hello **world**.

        - a
        - b

        ```csharp
        var x = 1;
        ```
        """;

    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .Build(),
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public async Task Append_then_complete_matches_one_shot_render()
    {
        var oneShot = new PuraMarkdownView { Options = CreateOptions() };
        oneShot.Markdown = Document;
        var expected = ControlTreeDumper.Dump(oneShot);

        var streamed = new PuraMarkdownView { Options = CreateOptions() };
        var session = streamed.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        foreach (var chunk in Chunk(Document, 7))
        {
            session.Append(chunk);
        }

        await session.CompleteAsync();

        Assert.Equal(expected, ControlTreeDumper.Dump(streamed));
    }

    [AvaloniaFact]
    public async Task Append_after_complete_is_rejected()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("hello");
        await session.CompleteAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => session.Append("more"));
        Assert.Contains("终态", exception.Message);
    }

    [AvaloniaFact]
    public async Task Streaming_choice_is_disabled_until_complete()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("""
            ```pura-agent
            { "type": "choice", "version": "1.0", "id": "c1", "question": "Q", "options": [ { "id": "a", "label": "A" } ] }
            ```
            """ + "\n\n");

        var radios = ControlTreeDumper.Find<Avalonia.Controls.RadioButton>(view).ToList();
        Assert.Single(radios);
        Assert.False(radios[0].IsEnabled);

        await session.CompleteAsync();

        var enabledRadios = ControlTreeDumper.Find<Avalonia.Controls.RadioButton>(view).ToList();
        Assert.Single(enabledRadios);
        Assert.True(enabledRadios[0].IsEnabled);
        Assert.Null(captured);
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}





