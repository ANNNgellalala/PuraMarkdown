using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace PuraMarkdown.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "measure")
        {
            Measure.Run();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

internal static class Measure
{
    public static void Run()
    {
        var pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build();
        var document = string.Concat(Enumerable.Range(0, 200).Select(i => $"paragraph {i} with **bold** and `code`.\n\n"));
        var window = document[Math.Max(0, document.Length - 8192)..];

        for (var i = 0; i < 200; i++)
        {
            pipeline.Parse(window);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        const int iterations = 2000;
        for (var i = 0; i < iterations; i++)
        {
            pipeline.Parse(window);
        }

        stopwatch.Stop();
        Console.WriteLine($"window={window.Length} chars, avg={stopwatch.Elapsed.TotalMilliseconds * 1000 / iterations:F2} us");

        stopwatch.Restart();
        for (var i = 0; i < 100; i++)
        {
            pipeline.Parse(document);
        }

        stopwatch.Stop();
        Console.WriteLine($"doc={document.Length} chars, avg={stopwatch.Elapsed.TotalMilliseconds * 1000 / 100:F2} us");
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class ParseBenchmarks
{
    private readonly PuraPipeline _pipeline = new PuraPipelineBuilder()
        .UseCommonMark()
        .UseGfm()
        .UseAgentComponents()
        .Build();

    private string _small = string.Empty;
    private string _large = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _small = BuildDocument(1024, 20);
        _large = BuildDocument(1024, 1000);
    }

    [Benchmark]
    public object Parse20Kb() => _pipeline.Parse(_small);

    [Benchmark]
    public object Parse1Mb() => _pipeline.Parse(_large);

    private static string BuildDocument(int paragraphSize, int paragraphs)
    {
        var builder = new StringBuilder(paragraphSize * paragraphs + 4);
        var body = new string('x', paragraphSize);
        for (var i = 0; i < paragraphs; i++)
        {
            builder.Append(body).Append("\n\n");
        }

        return builder.ToString();
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class StreamingScannerBenchmarks
{
    private readonly PuraPipeline _pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build();
    private string _document = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            builder.Append("paragraph ").Append(i).Append(" with **bold** and `code`.\n\n");
        }

        _document = builder.ToString();
    }

    [Benchmark]
    public int FindStableBoundary() => StreamingBlockScanner.FindStableBoundary(_document, 0);

    [Benchmark]
    public int IncrementalParseOfTailWindow()
    {
        // 尾部窗口 8KB 的解析成本（模拟每次追加后的窗口解析）。
        var start = Math.Max(0, _document.Length - 8192);
        return _pipeline.Parse(_document[start..]).Count;
    }
}



