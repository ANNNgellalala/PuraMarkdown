using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PuraMarkdown.Chat;
using PuraMarkdown.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class ChatViewTests
{
    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public void Chat_view_virtualizes_messages()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        for (var i = 0; i < 1000; i++)
        {
            chat.AddMessage(new PuraChatMessage
            {
                Role = PuraChatRole.User,
                Markdown = $"message {i}",
            });
        }

        var window = new Window { Width = 900, Height = 600, Content = chat };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var realized = window.GetVisualDescendants().OfType<PuraMarkdownView>().Count();
        Assert.InRange(realized, 1, 60);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Chat_streaming_completion_adds_history_message()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        var session = chat.BeginStreamingMessage(new PuraChatMessage { Role = PuraChatRole.Assistant });
        await session.AppendAsync("hello".AsMemory());
        await session.CompleteAsync();

        Assert.Single(chat.Messages);
        Assert.Equal("hello", chat.Messages[0].Markdown);
    }
}
