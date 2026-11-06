using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PuraMarkdown.Security;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class RenderingCompletenessTests
{
    private static PuraMarkdownOptions CreateOptions(PuraLimits? limits = null)
        => new()
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            Theme = PuraTheme.Light,
            Limits = limits ?? new PuraLimits(),
        };

    private static string TextOf(Control control)
        => control switch
        {
            TextBlock { Text: not null } text => text.Text!,
            TextBlock text => string.Concat(text.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text) ?? []),
            _ => string.Empty,
        };

    [AvaloniaFact]
    public void MaxBlocks_limits_rendered_blocks_and_warns()
    {
        var warnings = new List<string>();
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(new PuraLimits { MaxBlocks = 3 }),
        };
        view.Warning += (_, args) => warnings.Add(args.Warning.Message);
        view.Markdown = string.Concat(Enumerable.Range(0, 10).Select(i => $"paragraph {i}\n\n"));

        Assert.True(view.Blocks.Count <= 3, $"blocks={view.Blocks.Count}");
        Assert.Contains(warnings, message => message.Contains("块数量", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void MaxNestingDepth_blocks_deeply_nested_content()
    {
        var warnings = new List<string>();
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(new PuraLimits { MaxNestingDepth = 2 }),
        };
        view.Warning += (_, args) => warnings.Add(args.Warning.Message);
        view.Markdown = string.Concat(Enumerable.Repeat("> ", 10)) + "deep";

        Assert.Contains(warnings, message => message.Contains("嵌套深度", StringComparison.Ordinal));
        Assert.Empty(view.Blocks);
    }

    [AvaloniaFact]
    public void MaxFenceChars_truncates_code_block()
    {
        var warnings = new List<string>();
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(new PuraLimits { MaxFenceChars = 20 }),
        };
        view.Warning += (_, args) => warnings.Add(args.Warning.Message);
        view.Markdown = "```csharp\n" + new string('x', 100) + "\n```";

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("代码已截断", dump);
        Assert.Contains(warnings, message => message.Contains("代码围栏", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Autolink_is_clickable_and_respects_url_policy()
    {
        var allowed = new PuraMarkdownView { Options = CreateOptions(), Markdown = "https://example.com" };
        Assert.Single(ControlTreeDumper.Find<HyperlinkButton>(allowed));

        var blocked = new PuraMarkdownView { Options = CreateOptions(), Markdown = "ftp://example.com" };
        Assert.Empty(ControlTreeDumper.Find<HyperlinkButton>(blocked));
    }

    [AvaloniaFact]
    public void Table_applies_column_alignment_and_fills_missing_cells()
    {
        const string markdown = """
            | L | C | R |
            | :--- | :---: | ---: |
            | a | b | c |
            | d |
            """;
        var view = new PuraMarkdownView { Options = CreateOptions(), Markdown = markdown };

        var cellA = ControlTreeDumper.Find<TextBlock>(view).Single(block => TextOf(block) == "a");
        var cellB = ControlTreeDumper.Find<TextBlock>(view).Single(block => TextOf(block) == "b");
        var cellC = ControlTreeDumper.Find<TextBlock>(view).Single(block => TextOf(block) == "c");
        Assert.Equal(TextAlignment.Left, cellA.TextAlignment);
        Assert.Equal(TextAlignment.Center, cellB.TextAlignment);
        Assert.Equal(TextAlignment.Right, cellC.TextAlignment);

        var grid = ControlTreeDumper.Find<Grid>(view).Single(g => g.ColumnDefinitions.Count == 3);
        // 3 行（表头 + 2 行数据）× 3 列；短行也应补齐空单元格。
        Assert.Equal(9, grid.Children.Count);
    }

    [AvaloniaFact]
    public void Announce_updates_live_region()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Announce("done");

        var liveRegion = ControlTreeDumper.Find<TextBlock>(view)
            .Single(block => Equals(block.Tag, "pura-live-region"));
        Assert.Equal("done", liveRegion.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(liveRegion));
    }

    [AvaloniaFact]
    public async Task Abort_renders_current_content_and_announces()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        string? announcement = null;
        view.Announced += (_, message) => announcement = message;

        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("partial content");
        await session.AbortAsync();

        Assert.Contains("partial content", ControlTreeDumper.Dump(view));
        Assert.Equal("消息生成已中断", announcement);
    }

    [AvaloniaFact]
    public void Virtualized_component_state_is_restored_after_recreation()
    {
        const string markdown = """
            ```pura-agent
            { "type": "choice", "version": "1.0", "id": "c1", "question": "Q", "options": [ { "id": "a", "label": "A" } ] }
            ```
            """;
        var options = new PuraMarkdownOptions
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().UseAgentComponents().Build(),
            EnableBlockVirtualization = true,
        };
        var view = new PuraMarkdownView { Options = options };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        view.Markdown = markdown;
        Dispatcher.UIThread.RunJobs();

        var radio = ControlTreeDumper.Find<RadioButton>(view).Single();
        radio.IsChecked = true;
        Assert.True(radio.IsChecked);

        // 模拟虚拟化回收后重建：整篇重渲染，状态应从已上报事件中恢复。
        view.Markdown = null;
        view.Markdown = markdown;
        Dispatcher.UIThread.RunJobs();

        var restored = ControlTreeDumper.Find<RadioButton>(view).Single();
        Assert.True(restored.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void Reference_definition_from_committed_window_resolves_in_tail()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("[ref]: https://example.com\n\n");
        session.Append("see [ref]\n\n");

        var buttons = ControlTreeDumper.Find<HyperlinkButton>(view).ToList();
        Assert.True(buttons.Count == 1, ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public async Task Oversized_tail_pauses_refresh_and_completes()
    {
        var warnings = new List<string>();
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Warning += (_, args) => warnings.Add(args.Warning.Message);

        var session = view.BeginStreaming(new PuraStreamingOptions
        {
            Synchronous = true,
            MaxTailChars = 64,
        });
        session.Append("```csharp\n" + new string('x', 500));

        Assert.Empty(view.Blocks);
        await session.CompleteAsync();
        Assert.Single(view.Blocks);
        Assert.Contains(warnings, message => message.Contains("暂停尾部刷新", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Complete_can_be_called_from_background_thread()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming();
        session.Append("hello from background");

        await Task.Run(async () => await session.CompleteAsync());

        Assert.Contains("hello from background", ControlTreeDumper.Dump(view));
    }
}






