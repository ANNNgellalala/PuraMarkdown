using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PuraMarkdown.Agent.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class ConversationScaleTests
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

    [AvaloniaFact]
    public void ItemsRepeater_realizes_only_visible_messages()
    {
        var options = CreateOptions();
        var messages = Enumerable.Range(0, 1000)
            .Select(i => $"## Message {i}\n\nparagraph {i} with **bold** and `code`.")
            .ToList();

        var repeater = new ItemsRepeater
        {
            ItemsSource = messages,
            ItemTemplate = new FuncDataTemplate<string>((markdown, _) => new PuraMarkdownView
            {
                Options = options,
                Markdown = markdown,
            }),
        };
        repeater.Layout = new StackLayout();

        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = new ScrollViewer { Content = repeater },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var realized = window.GetVisualDescendants().OfType<PuraMarkdownView>().Count();
        Assert.InRange(realized, 1, 50);

        window.Close();
    }

    [AvaloniaFact]
    public void Many_message_views_render_independently()
    {
        var options = CreateOptions();
        var views = new List<PuraMarkdownView>();
        const int messageCount = 100;

        for (var i = 0; i < messageCount; i++)
        {
            var view = new PuraMarkdownView { Options = options };
            view.Markdown = $"""
                ## Message {i}

                Paragraph with **bold** and `code`.

                - item a
                - item b

                | A | B |
                | --- | --- |
                | {i} | ok |

                ```csharp
                var value = {i};
                ```
                """;
            view.Measure(new Avalonia.Size(900, double.PositiveInfinity));
            view.Arrange(new Avalonia.Rect(view.DesiredSize));
            views.Add(view);
        }

        Assert.Equal(messageCount, views.Count);
        Assert.All(views, view => Assert.True(view.Blocks.Count >= 5, $"blocks={view.Blocks.Count}"));
        Assert.Contains("Message 99", ControlTreeDumper.Dump(views[^1]));
    }
}

