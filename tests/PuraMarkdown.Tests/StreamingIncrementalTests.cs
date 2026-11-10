using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PuraMarkdown.Extension.Highlighting.ColorCode;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using PuraMarkdown.Agent;
using PuraMarkdown.Agent.Testing;
using Xunit;
using PuraMarkdown.Core;

namespace PuraMarkdown.Tests;

public sealed class StreamingIncrementalTests
{
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

    [Fact]
    public void Scanner_commits_after_stable_paragraph()
    {
        Assert.Equal(3, StreamingBlockScanner.FindStableBoundary("a\n\nb", 0));
    }

    [Fact]
    public void Scanner_keeps_list_in_tail()
    {
        Assert.Equal(0, StreamingBlockScanner.FindStableBoundary("- a\n\nb", 0));
    }

    [Fact]
    public void Scanner_keeps_open_fence_in_tail()
    {
        Assert.Equal(0, StreamingBlockScanner.FindStableBoundary("```csharp\na\n\n", 0));
    }

    [Fact]
    public void Scanner_commits_after_closed_fence()
    {
        const string doc = "```csharp\na\n```\n\nnext";
        Assert.Equal(doc.Length - "next".Length, StreamingBlockScanner.FindStableBoundary(doc, 0));
    }

    [AvaloniaFact]
    public async Task Incremental_streaming_commits_prefix_and_parses_less_than_full()
    {
        var paragraph = new string('x', 500);
        var text = $"# Title\n\n{paragraph}\n\nsecond paragraph\n\nthird paragraph\n";
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });

        foreach (var chunk in Chunk(text, 64))
        {
            session.Append(chunk);
        }

        var stats = session.Statistics;
        Assert.True(stats.Commits > 0, "应至少提交一次稳定块");
        Assert.True(stats.CommittedChars > 0);
        Assert.True(stats.CommittedParsedChars < text.Length, $"稳定块只解析一次（{stats.CommittedParsedChars}）");
        Assert.True(stats.TotalParsedChars < text.Length * 8, $"总解析量应可控（{stats.TotalParsedChars}）");

        await session.CompleteAsync();
    }

    [AvaloniaFact]
    public void Streaming_prefix_is_rendered_before_complete()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("first paragraph\n\nsecond");

        // 第一段已稳定并提交，第二段仍在尾部窗口。
        Assert.Contains("first paragraph", ControlTreeDumper.Dump(view));
        Assert.Contains("second", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Streaming_tail_is_replaced_not_duplicated()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });

        session.Append("hello");
        session.Append(" world");

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("hello world", dump);
        Assert.Equal(1, CountOccurrences(dump, "hello"));
    }

    [AvaloniaFact]
    public void Prewarm_renders_representative_blocks_without_side_effects()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Prewarm("| A | B |\n| --- | --- |\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```\n");

        Assert.Empty(view.Blocks);
        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void Committed_block_replaces_streaming_tail_without_duplicate()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });

        session.Append("```csharp\nvar va");
        session.Append("lue = 1;\n```\n\nnext paragraph");

        var dump = ControlTreeDumper.Dump(view);
        Assert.Equal(1, CountOccurrences(dump, "var value = 1;"));
        Assert.Equal(1, CountOccurrences(dump, "next paragraph"));
        Assert.Equal(2, view.Blocks.Count);
    }

    [AvaloniaFact]
    public void Streaming_code_block_is_highlighted_before_complete()
    {
        var options = new PuraMarkdownOptions
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UsePlugin(new ColorCodePlugin())
                .Build(),
            Theme = PuraTheme.Light,
        };

        var view = new PuraMarkdownView { Options = options };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("```csharp\nvar value = 1;\n```");

        var runs = ControlTreeDumper.Find<TextBlock>(view)
            .SelectMany(text => text.Inlines?.OfType<Run>() ?? [])
            .ToList();

        Assert.Contains(runs, run => run.Foreground is ISolidColorBrush brush
                                     && brush.Color != PuraTheme.Light.Colors.Text);
    }

    [AvaloniaFact]
    public async Task Async_streaming_highlights_code_before_complete()
    {
        var options = new PuraMarkdownOptions
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UsePlugin(new ColorCodePlugin())
                .Build(),
            Theme = PuraTheme.Light,
        };

        var view = new PuraMarkdownView { Options = options };
        var session = view.BeginStreaming();
        session.Append("```csharp\nvar value = 1;\n```");

        var stopwatch = Stopwatch.StartNew();
        var colored = false;
        while (!colored && stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            colored = ControlTreeDumper.Find<TextBlock>(view)
                .SelectMany(text => text.Inlines?.OfType<Run>() ?? [])
                .Any(run => run.Foreground is ISolidColorBrush brush
                            && brush.Color != PuraTheme.Light.Colors.Text);
            await Task.Delay(10);
        }

        Assert.True(colored, $"流式高亮未生效：{session.LastWorkerError}");
        await session.CompleteAsync();
    }

    [AvaloniaFact]
    public async Task Async_streaming_applies_intermediate_updates()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming();
        session.Append("hello");

        var stopwatch = Stopwatch.StartNew();
        while (session.Statistics.FlushCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(session.Statistics.FlushCount > 0, $"后台 worker 未处理追加内容：{session.LastWorkerError}");
        stopwatch.Restart();
        while (view.Blocks.Count == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.Contains("hello", ControlTreeDumper.Dump(view));

        session.Append(" world");
        stopwatch.Restart();
        while (!ControlTreeDumper.Dump(view).Contains("hello world", StringComparison.Ordinal)
               && stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.Contains("hello world", ControlTreeDumper.Dump(view));
        await session.CompleteAsync();
    }

    [AvaloniaFact]
    public async Task Async_complete_renders_large_document_in_chunks()
    {
        var text = string.Concat(Enumerable.Repeat("paragraph with **bold** and `code`.\n\n", 600));
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming();
        for (var i = 0; i < text.Length; i += 4096)
        {
            session.Append(text.Substring(i, Math.Min(4096, text.Length - i)));
        }

        var stopwatch = Stopwatch.StartNew();
        await session.CompleteAsync();
        stopwatch.Stop();

        Assert.True(view.Blocks.Count >= 600, $"blocks={view.Blocks.Count}");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Complete 耗时过长：{stopwatch.Elapsed}");
    }

    [AvaloniaFact]
    public async Task Async_streaming_completes_like_one_shot()
    {
        const string document = "# Async\n\nparagraph with **bold**\n\n- item a\n- item b\n\n```csharp\nvar x = 1;\n```\n";
        var expected = new PuraMarkdownView { Options = CreateOptions() };
        expected.Markdown = document;

        var streamed = new PuraMarkdownView { Options = CreateOptions() };
        var session = streamed.BeginStreaming();
        for (var i = 0; i < document.Length; i += 7)
        {
            session.Append(document.Substring(i, Math.Min(7, document.Length - i)));
        }

        await session.CompleteAsync();

        Assert.Equal(ControlTreeDumper.Dump(expected), ControlTreeDumper.Dump(streamed));
    }

    [AvaloniaFact]
    public async Task Complete_announces()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        string? announcement = null;
        view.Announced += (_, message) => announcement = message;

        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("hi");
        await session.CompleteAsync();

        Assert.Equal("消息内容生成完成", announcement);
    }

    [AvaloniaFact]
    public void Keyboard_space_selects_radio()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            { "type": "choice", "version": "1.0", "id": "kb", "question": "Q", "options": [ { "id": "a", "label": "A" } ] }
            ```
            """;

        var window = new Window { Content = view, Width = 480, Height = 640 };
        window.Show();
        window.Activate();

        var radio = ControlTreeDumper.Find<RadioButton>(view).Single();
        Assert.True(radio.Focus());

        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");

        if (captured is null)
        {
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        }

        window.Close();

        Assert.NotNull(captured);
        Assert.Equal(["a"], captured!.SelectedIds);
    }

    [AvaloniaFact]
    public async Task Random_chunking_matches_one_shot()
    {
        const string document = """
            # Streaming

            Paragraph one with **bold**.

            - item a
            - item b

            | A | B |
            | --- | --- |
            | 1 | 2 |

            ```csharp
            var x = 1;
            ```
            """;

        var oneShot = new PuraMarkdownView { Options = CreateOptions() };
        oneShot.Markdown = document;
        var expected = ControlTreeDumper.Dump(oneShot);

        foreach (var seed in new[] { 1, 7, 42, 99 })
        {
            var streamed = new PuraMarkdownView { Options = CreateOptions() };
            var session = streamed.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
            var random = new Random(seed);
            var offset = 0;
            while (offset < document.Length)
            {
                var size = random.Next(1, 23);
                var length = Math.Min(size, document.Length - offset);
                session.Append(document.Substring(offset, length));
                offset += length;
            }

            await session.CompleteAsync();
            Assert.Equal(expected, ControlTreeDumper.Dump(streamed));
        }
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}














