using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using PuraMarkdown.Diagrams.Mermaid;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class MermaidAndPerformanceTests
{
    private static PuraMarkdownOptions CreateOptions(bool withMermaid = true)
    {
        var builder = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseAgentComponents();
        if (withMermaid)
        {
            builder.UsePlugin(new MermaidPlugin());
        }

        return new PuraMarkdownOptions { Pipeline = builder.Build(), Theme = PuraTheme.Light };
    }

    [AvaloniaFact]
    public void Mermaid_plugin_renders_flowchart_via_real_engine()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "```mermaid\ngraph TD\n  A[Start] --> B[End]\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("Mermaid · Mermaider", dump);
        Assert.Contains("Start", dump);
        Assert.Contains("End", dump);
    }

    [AvaloniaFact]
    public void Mermaid_invalid_source_falls_back_gracefully()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "```mermaid\nthis is not a valid mermaid diagram !!!\n```";

        var dump = ControlTreeDumper.Dump(view);

        // 解析失败时显示错误卡（包含 "图表解析失败" 或原始源码），不抛异常。
        Assert.True(dump.Contains("图表解析失败", StringComparison.Ordinal)
                    || dump.Contains("this is not a valid mermaid diagram", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Mermaid_without_plugin_renders_code_block()
    {
        var view = new PuraMarkdownView { Options = CreateOptions(withMermaid: false) };
        view.Markdown = "```mermaid\ngraph TD\n  A --> B\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("graph TD", dump);
        Assert.DoesNotContain("Mermaid · Mermaider", dump);
    }

    [AvaloniaFact]
    public async Task Large_streaming_remains_incremental()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 600; i++)
        {
            builder.Append("paragraph ").Append(i).Append(" with some **bold** text and `code`.\n\n");
        }

        var text = builder.ToString();
        var view = new PuraMarkdownView { Options = CreateOptions(withMermaid: false) };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < text.Length; i += 2048)
        {
            session.Append(text.Substring(i, Math.Min(2048, text.Length - i)));
        }

        var stats = session.Statistics;
        await session.CompleteAsync();
        stopwatch.Stop();

        Assert.True(stats.Commits > 10, $"应多次提交稳定块（{stats.Commits}）");
        Assert.True(stats.CommittedParsedChars <= text.Length + 4096, $"稳定块只解析一次（{stats.CommittedParsedChars}/{text.Length}）");
        Assert.True(stats.TotalParsedChars < text.Length * 3, $"总解析量应可控（{stats.TotalParsedChars}/{text.Length}）");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"流式耗时过长：{stopwatch.Elapsed}");
    }

    [AvaloniaFact]
    public async Task One_megabyte_document_streams_within_budget()
    {
        // 1000 个约 1KB 的段落，总计约 1MB：验证"大文档 + 1000 块"的增量路径。
        var paragraph = new string('x', 1000);
        var builder = new StringBuilder(1024 * 1100);
        for (var i = 0; i < 1000; i++)
        {
            builder.Append(paragraph).Append("\n\n");
        }

        var text = builder.ToString();
        Assert.True(text.Length >= 1_000_000, $"应约为 1MB（{text.Length}）");

        var view = new PuraMarkdownView { Options = CreateOptions(withMermaid: false) };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < text.Length; i += 8192)
        {
            session.Append(text.Substring(i, Math.Min(8192, text.Length - i)));
        }

        var stats = session.Statistics;
        await session.CompleteAsync();
        stopwatch.Stop();

        Assert.True(stats.Commits >= 100, $"应多次提交稳定块（{stats.Commits}）");
        Assert.True(stats.CommittedParsedChars <= text.Length + 8192, $"稳定块只解析一次（{stats.CommittedParsedChars}/{text.Length}）");
        Assert.True(stats.TotalParsedChars < text.Length * 2, $"总解析量应接近文本长度（{stats.TotalParsedChars}/{text.Length}）");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"1MB 流式耗时过长：{stopwatch.Elapsed}");
        Assert.Equal(1000, view.Blocks.Count);
    }
}

