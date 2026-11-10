using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CSharpMath.Avalonia;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using PuraMarkdown.Plugins.Samples;
using PuraMarkdown.Core;
using PuraMarkdown.Agent.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class EmojiMathTests
{
    private static PuraMarkdownOptions CreateOptionsCore(bool enableSmileys = false)
    {
        var builder = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji(enableSmileys)
            .UseMathematics();
        return new PuraMarkdownOptions { Pipeline = builder.Build(), Theme = PuraTheme.Light };
    }

    private static PuraMarkdownOptions CreateOptionsWithCSharpMath()
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseMathematics()
                .UsePlugin(new CSharpMathMathPlugin())
                .Build(),
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public void Emoji_shortcode_node_is_supported_and_renders_unicode()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().UseEmoji().Build();
        var document = syntax.ParseDocument("Hello :smile: :fire:");

        var emoji = document.Markdown.Descendants<EmojiInline>().ToArray();
        Assert.Equal(2, emoji.Length);
        Assert.Equal(
            PuraNodeSupport.Rendered,
            MarkdigCompatibility.Describe(emoji[0])!.Support);

        var view = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "Hello :smile: :fire:",
        };
        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("😄", dump, StringComparison.Ordinal);
        Assert.Contains("🔥", dump, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Emoji_smileys_are_opt_in()
    {
        var plain = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "收到 :)",
        };
        Assert.DoesNotContain("😃", ControlTreeDumper.Dump(plain), StringComparison.Ordinal);
        Assert.Contains(":)", ControlTreeDumper.Dump(plain), StringComparison.Ordinal);

        var smileys = new PuraMarkdownView
        {
            Options = CreateOptionsCore(enableSmileys: true),
            Markdown = "收到 :)",
        };
        Assert.Contains("😃", ControlTreeDumper.Dump(smileys), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Emoji_in_code_span_is_not_converted()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "`:smile:` 与 `:)`",
        };
        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains(":smile:", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("😄", dump, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Emoji_inside_strong_is_not_bolded()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "**:smile:**",
        };

        var runs = ControlTreeDumper.Find<Avalonia.Controls.TextBlock>(view)
            .SelectMany(block => block.Inlines?.OfType<Avalonia.Controls.Documents.Run>() ?? [])
            .Where(run => run.Text == "😄")
            .ToArray();

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.Equal(FontWeight.Normal, run.FontWeight));
    }

    [Fact]
    public void Math_nodes_are_supported_and_described()
    {
        var syntax = new PuraSyntaxBuilder().UseCommonMark().UseGfm().UseMathematics().Build();
        var document = syntax.ParseDocument("$E=mc^2$\n\n$$\n\\int_0^1 x^2 dx\n$$");

        foreach (var node in document.Markdown.Descendants())
        {
            Assert.True(MarkdigCompatibility.IsSupported(node), node.GetType().Name);
        }

        Assert.Equal(
            PuraNodeSupport.Rendered,
            MarkdigCompatibility.Describe(document.Markdown.Descendants<MathInline>().Single())!.Support);
        Assert.Equal(
            PuraNodeSupport.Rendered,
            MarkdigCompatibility.Describe(document.Markdown.Descendants<MathBlock>().Single())!.Support);
    }

    [AvaloniaFact]
    public void Math_falls_back_to_tex_text_by_default()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "质能方程 $E=mc^2$ 成立。\n\n$$\n\\int_0^1 x^2 dx\n$$",
        };

        Assert.Empty(ControlTreeDumper.Find<MathView>(view));
        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("E=mc^2", dump, StringComparison.Ordinal);
        Assert.Contains("\\int", dump, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void CSharpMath_sample_plugin_renders_mathview()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptionsWithCSharpMath(),
            Markdown = "质能方程 $E=mc^2$ 成立。\n\n$$\n\\int_0^1 x^2 dx\n$$",
        };

        var mathViews = ControlTreeDumper.Find<MathView>(view).ToList();
        Assert.Equal(2, mathViews.Count);
        var latex = string.Join(" | ", mathViews.Select(mathView => mathView.LaTeX));
        Assert.Contains("E=mc^2", latex, StringComparison.Ordinal);
        Assert.Contains("\\int", latex, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Currency_with_dollar_signs_is_not_treated_as_math()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptionsCore(),
            Markdown = "价格 $5 与 $10。",
        };

        Assert.Empty(ControlTreeDumper.Find<MathView>(view));
        Assert.Contains("$5", ControlTreeDumper.Dump(view), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Math_streaming_with_blank_line_matches_one_shot()
    {
        const string document = "# 公式\n\n$$\na\n\nb\n$$\n\ntail";

        var oneShot = new PuraMarkdownView { Options = CreateOptionsCore(), Markdown = document };
        var expected = ControlTreeDumper.Dump(oneShot);

        var streamed = new PuraMarkdownView { Options = CreateOptionsCore() };
        var session = streamed.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        foreach (var chunk in Chunk(document, 4))
        {
            session.Append(chunk);
        }

        await session.CompleteAsync();
        Assert.Equal(expected, ControlTreeDumper.Dump(streamed));
    }

    [AvaloniaFact]
    public void Math_tail_renders_text_instead_of_mathview()
    {
        var view = new PuraMarkdownView { Options = CreateOptionsCore() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("$$\n\\int_0^1 x dx\n");

        // 未闭合数学块在尾部窗口中应以 TeX 文本回退，不实例化 MathView。
        Assert.Empty(ControlTreeDumper.Find<MathView>(view));
        Assert.Contains("\\int_0^1", ControlTreeDumper.Dump(view), StringComparison.Ordinal);
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}
