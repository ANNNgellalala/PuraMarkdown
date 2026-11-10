using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;

namespace PuraMarkdown.Spike;

internal sealed class SpikeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class Program
{
    private const string Sample = """
        # PuraMarkdown spike

        这是 **M2** 纵向切片：Markdig → 默认渲染 → 块宿主 → `pura-agent` → 插件（高亮/图表/自定义组件）。

        - 解析路由
        - 渲染器注册表
        - 插件：ColorCode / rating / 示例图表

        | 项 | 状态 |
        | --- | --- |
        | Markdig | ok |
        | Avalonia | ok |

        Emoji 与行内公式：:smile: :fire: 质能方程 $E=mc^2$。

        $$
        \int_0^1 x^2 \, dx = \frac{1}{3}
        $$

        ```csharp
        var pipeline = new PuraPipelineBuilder().UseGfm().Build();
        ```

        ```mermaid
        graph LR
          A[Markdig] --> B[Plugin]
        ```

        ```pura-agent
        {
          "type": "choice",
          "version": "1.0",
          "id": "next",
          "question": "下一步做什么？",
          "options": [
            { "id": "m2", "label": "进入 M2（插件）" },
            { "id": "docs", "label": "先补文档" }
          ]
        }
        ```

        ```pura-agent
        { "type": "rating", "version": "1.0", "id": "quality", "label": "本次输出质量", "max": 5, "value": 4 }
        ```
        """;

    public static void Main(string[] args)
    {
        var screenshot = args.Length > 0 && args[0] == "--screenshot";
        var dark = args.Contains("--dark");
        var builder = AppBuilder.Configure<SpikeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !screenshot });
        if (screenshot)
        {
            builder = builder.UseSkia();
        }

        builder.SetupWithoutStarting();

        if (screenshot)
        {
            RenderScreenshot(dark, args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "docs/images/demo.png");
            return;
        }

        if (args.Length > 0 && args[0] == "--stream-perf")
        {
            StreamPerf();
            StreamPerf();
            return;
        }

        if (args.Length > 0 && args[0] == "--stream-perf-long")
        {
            StreamPerfLong(args.Contains("--no-highlight"));
            return;
        }

        if (args.Length > 0 && args[0] == "--conversation-perf")
        {
            ConversationPerf();
            return;
        }

        if (args.Length > 0 && args[0] == "--conversation-perf-large")
        {
            ConversationPerfLarge();
            return;
        }

        if (args.Length > 0 && args[0] == "--virtualization-perf")
        {
            VirtualizationPerf();
            return;
        }

        if (args.Length > 0 && args[0] == "--block-virtualization-perf")
        {
            BlockVirtualizationPerf();
            return;
        }

        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji()
            .UseMathematics()
            .UsePlugin(new PuraMarkdown.Extension.Highlighting.ColorCode.ColorCodePlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.RatingPlugin())
            .UsePlugin(new PuraMarkdown.Extension.Diagrams.Mermaid.MermaidPlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.CSharpMathMathPlugin())
            .Build();

        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions
            {
                Pipeline = pipeline,
                Theme = PuraTheme.Light,
            },
        };

        view.InteractionOccurred += (_, args) =>
        {
            Console.WriteLine($"[interaction] type={args.ComponentType} id={args.ComponentId} selected={string.Join(',', args.SelectedIds)} confirmed={args.Confirmed}");
            if (args.ComponentType == "choice")
            {
                Console.WriteLine($"可选动作：进入 M2（{string.Join(", ", args.SelectedIds)}）");
            }

            if (args.ComponentType == "rating")
            {
                Console.WriteLine($"评分：{string.Join(", ", args.SelectedIds)}");
            }
        };

        view.Markdown = Sample;

        Console.WriteLine("=== 控件树 dump ===");
        Console.WriteLine(Dump(view));
        Console.WriteLine($"=== blocks: {view.Blocks.Count} ===");
    }

    private static void StreamPerf()
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji()
            .UseMathematics()
            .UsePlugin(new PuraMarkdown.Extension.Highlighting.ColorCode.ColorCodePlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.RatingPlugin())
            .UsePlugin(new PuraMarkdown.Extension.Diagrams.Mermaid.MermaidPlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.CSharpMathMathPlugin())
            .Build();

        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = pipeline, Theme = PuraTheme.Light },
        };
        view.Prewarm(Sample);

        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });

        const int chunk = 16;
        var times = new List<double>(Sample.Length / chunk + 1);
        var total = Stopwatch.StartNew();
        for (var i = 0; i < Sample.Length; i += chunk)
        {
            var length = Math.Min(chunk, Sample.Length - i);
            var start = Stopwatch.GetTimestamp();
            session.Append(Sample.Substring(i, length));
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            times.Add(elapsed);
            if (elapsed > 5)
            {
                var startPos = Math.Max(0, i - 12);
                var preview = Sample.Substring(startPos, Math.Min(70, Sample.Length - startPos)).Replace("\n", "\\n");
                Console.WriteLine($"  slow append idx={i} len={length} {elapsed:F1}ms committed={session.Statistics.CommittedChars} tail={session.Statistics.TailChars} :: {preview}");
            }
        }

        var completeStart = Stopwatch.GetTimestamp();
        session.CompleteAsync().GetAwaiter().GetResult();
        var completeMs = Stopwatch.GetElapsedTime(completeStart).TotalMilliseconds;
        total.Stop();
        times.Sort();
        var stats = session.Statistics;
        Console.WriteLine($"appends={times.Count} total={total.Elapsed.TotalMilliseconds:F1}ms p50={Percentile(times, 0.5):F3} p95={Percentile(times, 0.95):F3} max={times[^1]:F3}ms");
        Console.WriteLine($"complete={completeMs:F1}ms commits={stats.Commits} committed={stats.CommittedChars} tail={stats.TailChars} committedParsed={stats.CommittedParsedChars} tailParsed={stats.TailParsedChars}");
    }

    private static PuraMarkdownOptions ConversationOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .UsePlugin(new PuraMarkdown.Extension.Highlighting.ColorCode.ColorCodePlugin())
                .Build(),
        };

    private static string ConversationMessage(int index, int padding = 0)
    {
        var extra = padding > 0 ? new string('x', padding) : string.Empty;
        return $"""
            ## Message {index}

            Paragraph {index} with **bold**, `code` and [link](https://example.com/{index}). {extra}

            - item a
            - item b

            > quoted line {index}

            | A | B |
            | --- | --- |
            | {index} | ok |

            ```csharp
            var value{index} = {index};
            Console.WriteLine(value{index});
            ```
            """;
    }

    private static void LayoutView(PuraMarkdownView view)
    {
        view.Measure(new Size(920, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
    }

    private static double PercentileOf(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p) - 1)];
    }

    private sealed record ChatMessage(int Index)
    {
        public string Markdown => ConversationMessage(Index);
    }

    private static void BlockVirtualizationPerf()
    {
        const int blockCount = 1000;
        const int blockSize = 1000;
        var paragraph = new string('x', blockSize);
        var text = string.Concat(Enumerable.Range(0, blockCount).Select(_ => paragraph + "\n\n"));

        var options = new PuraMarkdownOptions
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            EnableBlockVirtualization = true,
        };

        var before = GC.GetTotalMemory(true);
        var view = new PuraMarkdownView { Options = options, Markdown = text };
        var window = new Window
        {
            Width = 920,
            Height = 700,
            Content = view,
        };

        var total = Stopwatch.StartNew();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        total.Stop();
        var realized = window.GetVisualDescendants().OfType<TextBlock>().Count();
        var memory = GC.GetTotalMemory(true) - before;
        Console.WriteLine($"[块级虚拟化] blocks={view.Blocks.Count} realizedTextBlocks={realized} chars={text.Length} open={total.Elapsed.TotalMilliseconds:F0}ms mem={memory / 1024.0 / 1024.0:F1}MB");
        GC.KeepAlive(window);
        GC.KeepAlive(view);
        GC.Collect();

        var eagerOptions = new PuraMarkdownOptions
        {
            Pipeline = options.Pipeline,
        };
        var eagerBefore = GC.GetTotalMemory(true);
        var eagerView = new PuraMarkdownView { Options = eagerOptions, Markdown = text };
        var eagerWindow = new Window { Width = 920, Height = 700, Content = new ScrollViewer { Content = eagerView } };
        var eagerWatch = Stopwatch.StartNew();
        eagerWindow.Show();
        Dispatcher.UIThread.RunJobs();
        eagerWatch.Stop();
        var eagerTextBlocks = eagerWindow.GetVisualDescendants().OfType<TextBlock>().Count();
        var eagerMemory = GC.GetTotalMemory(true) - eagerBefore;
        Console.WriteLine($"[非虚拟化] blocks={eagerView.Blocks.Count} textBlocks={eagerTextBlocks} open={eagerWatch.Elapsed.TotalMilliseconds:F0}ms mem={eagerMemory / 1024.0 / 1024.0:F1}MB");
    }

    private static void VirtualizationPerf()
    {
        var options = ConversationOptions();
        var messages = Enumerable.Range(0, 1000).Select(i => new ChatMessage(i)).ToList();

        var before = GC.GetTotalMemory(true);
        var repeater = new ItemsRepeater
        {
            ItemsSource = messages,
            ItemTemplate = new FuncDataTemplate<ChatMessage>((message, _) => new PuraMarkdownView
            {
                Options = options,
                Markdown = message.Markdown,
            }),
        };
        repeater.Layout = new StackLayout();

        var window = new Window
        {
            Width = 920,
            Height = 700,
            Content = new ScrollViewer { Content = repeater },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var realizedViews = window.GetVisualDescendants().OfType<PuraMarkdownView>().ToList();
        var memory = GC.GetTotalMemory(true) - before;
        Console.WriteLine($"[虚拟化] messages={messages.Count} realizedViews={realizedViews.Count} blocks={realizedViews.Sum(v => v.Blocks.Count)} mem={memory / 1024.0 / 1024.0:F1}MB");
        GC.KeepAlive(window);
        GC.KeepAlive(messages);
    }

    private static void ConversationPerf() => RunConversation(perViewCount: 300, accumulatedCount: 150, streamedCount: 500, perMessageStreamCount: 120, padding: 0);

    private static void ConversationPerfLarge() => RunConversation(perViewCount: 1000, accumulatedCount: 60, streamedCount: 800, perMessageStreamCount: 200, padding: 1400);

    private static void RunConversation(int perViewCount, int accumulatedCount, int streamedCount, int perMessageStreamCount, int padding)
    {
        var options = ConversationOptions();
        var sample = ConversationMessage(0, padding);
        var warm = new PuraMarkdownView { Options = options };
        warm.Prewarm(sample);

        RunPerMessageViews(options, perViewCount, padding);
        GC.Collect();
        RunAccumulatedView(options, accumulatedCount, padding);
        GC.Collect();
        RunStreamingSession(options, streamedCount, padding);
        GC.Collect();
        RunPerMessageStreaming(options, perMessageStreamCount, padding);
    }

    private static void RunPerMessageViews(PuraMarkdownOptions options, int count, int padding)
    {
        var views = new List<PuraMarkdownView>(count);
        var times = new List<double>(count);
        var before = GC.GetTotalMemory(true);
        var total = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var view = new PuraMarkdownView { Options = options };
            view.Markdown = ConversationMessage(i, padding);
            LayoutView(view);
            views.Add(view);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        total.Stop();
        var memory = GC.GetTotalMemory(true) - before;
        Console.WriteLine($"[A 每消息独立控件] messages={count} total={total.Elapsed.TotalMilliseconds:F0}ms avg={times.Average():F2}ms p95={PercentileOf(times, 0.95):F2}ms mem={memory / 1024.0 / 1024.0:F1}MB blocks={views.Sum(v => v.Blocks.Count)}");
        GC.KeepAlive(views);
    }

    private static void RunAccumulatedView(PuraMarkdownOptions options, int count, int padding)
    {
        var view = new PuraMarkdownView { Options = options };
        var sb = new StringBuilder();
        var times = new List<double>(count);
        var total = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            sb.Append(ConversationMessage(i, padding)).Append("\n\n");
            var start = Stopwatch.GetTimestamp();
            view.Markdown = sb.ToString();
            LayoutView(view);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        total.Stop();
        Console.WriteLine($"[B 单控件累积全文] messages={count} chars={sb.Length} total={total.Elapsed.TotalMilliseconds:F0}ms last10avg={times.TakeLast(10).Average():F2}ms p95={PercentileOf(times, 0.95):F2}ms");
    }

    private static void RunStreamingSession(PuraMarkdownOptions options, int count, int padding)
    {
        var view = new PuraMarkdownView { Options = options };
        view.Prewarm(ConversationMessage(0, padding));
        var before = GC.GetTotalMemory(true);
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        var times = new List<double>(count);
        var total = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            session.Append(ConversationMessage(i, padding) + "\n\n");
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        var completeStart = Stopwatch.GetTimestamp();
        session.CompleteAsync().GetAwaiter().GetResult();
        var completeMs = Stopwatch.GetElapsedTime(completeStart).TotalMilliseconds;
        total.Stop();
        var memory = GC.GetTotalMemory(true) - before;
        Console.WriteLine($"[C 单流式会话] messages={count} total={total.Elapsed.TotalMilliseconds:F0}ms p95={PercentileOf(times, 0.95):F2}ms max={times.Max():F1}ms complete={completeMs:F0}ms blocks={view.Blocks.Count} mem={memory / 1024.0 / 1024.0:F1}MB");
    }

    private static void RunPerMessageStreaming(PuraMarkdownOptions options, int count, int padding)
    {
        var views = new List<PuraMarkdownView>(count);
        var times = new List<double>(count);
        var total = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var view = new PuraMarkdownView { Options = options };
            var message = ConversationMessage(i, padding);
            var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
            for (var offset = 0; offset < message.Length; offset += 32)
            {
                session.Append(message.Substring(offset, Math.Min(32, message.Length - offset)));
            }

            session.CompleteAsync().GetAwaiter().GetResult();
            LayoutView(view);
            views.Add(view);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        total.Stop();
        Console.WriteLine($"[D 每消息独立流式] messages={count} total={total.Elapsed.TotalMilliseconds:F0}ms avg={times.Average():F2}ms p95={PercentileOf(times, 0.95):F2}ms blocks={views.Sum(v => v.Blocks.Count)}");
        GC.KeepAlive(views);
    }
    private static void StreamPerfLong(bool noHighlight)
    {
        var lines = Enumerable.Range(0, 500).Select(i => $"var value{i} = Compute({i});");
        var sample = "# Long code block\n\n```csharp\n" + string.Join("\n", lines) + "\n```\n\nfinal paragraph\n";
        Console.WriteLine($"document={sample.Length} chars, highlight={!noHighlight}");

        var builder = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseAgentComponents();
        if (!noHighlight)
        {
            builder.UsePlugin(new PuraMarkdown.Extension.Highlighting.ColorCode.ColorCodePlugin());
        }

        var pipeline = builder.Build();
        var view = new PuraMarkdownView { Options = new PuraMarkdownOptions { Pipeline = pipeline } };
        view.Prewarm(sample);
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        var times = new List<double>(sample.Length / 16 + 1);
        var total = Stopwatch.StartNew();

        for (var i = 0; i < sample.Length; i += 16)
        {
            var length = Math.Min(16, sample.Length - i);
            var start = Stopwatch.GetTimestamp();
            session.Append(sample.Substring(i, length));
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        var completeStart = Stopwatch.GetTimestamp();
        session.CompleteAsync().GetAwaiter().GetResult();
        var completeMs = Stopwatch.GetElapsedTime(completeStart).TotalMilliseconds;
        total.Stop();
        times.Sort();
        Console.WriteLine($"appends={times.Count} total={total.Elapsed.TotalMilliseconds:F0}ms p50={Percentile(times, 0.5):F1} p95={Percentile(times, 0.95):F1} p99={Percentile(times, 0.99):F1} max={times[^1]:F0}ms");
    }

    private static double Percentile(List<double> sorted, double p)
        => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p) - 1)];

    private static void RenderScreenshot(bool dark, string path)
    {
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji()
            .UseMathematics()
            .UsePlugin(new PuraMarkdown.Extension.Highlighting.ColorCode.ColorCodePlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.RatingPlugin())
            .UsePlugin(new PuraMarkdown.Extension.Diagrams.Mermaid.MermaidPlugin())
            .UsePlugin(new PuraMarkdown.Plugins.Samples.CSharpMathMathPlugin())
            .Build();

        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions
            {
                Pipeline = pipeline,
                Theme = dark ? PuraTheme.Dark : PuraTheme.Light,
            },
        };
        view.Markdown = Sample;

        var window = new Window
        {
            Width = 920,
            Height = 980,
            Title = "PuraMarkdown",
            Content = new ScrollViewer
            {
                Padding = new Thickness(20),
                Content = view,
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.WriteLine("capture returned null");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        frame.Save(fullPath, new PngBitmapEncoderOptions());
        Console.WriteLine($"saved {fullPath} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    }

    private static string Dump(Control root)
    {
        var builder = new StringBuilder();
        Walk(root, 0, builder);
        return builder.ToString();
    }

    private static void Walk(Control control, int depth, StringBuilder builder)
    {
        builder.Append(' ', depth * 2).Append(control.GetType().Name);
        switch (control)
        {
            case TextBlock textBlock:
                builder.Append(" text=\"").Append(GetText(textBlock)).Append('"');
                break;
            case ToggleButton toggle:
                builder.Append(" checked=").Append(toggle.IsChecked).Append(" enabled=").Append(toggle.IsEnabled);
                break;
            case Button button:
                builder.Append(" content=\"").Append(button.Content).Append("\" enabled=").Append(button.IsEnabled);
                break;
        }

        builder.AppendLine();

        foreach (var child in Children(control))
        {
            Walk(child, depth + 1, builder);
        }
    }

    private static IEnumerable<Control> Children(Control control) => control switch
    {
        Panel panel => panel.Children.OfType<Control>(),
        Border { Child: Control borderChild } => [borderChild],
        ScrollViewer { Content: Control scrollChild } => [scrollChild],
        ContentControl { Content: Control contentChild } => [contentChild],
        _ => [],
    };

    private static string GetText(TextBlock textBlock)
        => textBlock.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
            : textBlock.Text ?? string.Empty;
}




















