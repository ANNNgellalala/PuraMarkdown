using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PuraMarkdown.Agent.Chat;
using PuraMarkdown.Agent.Testing;
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
    public void Chat_message_operations_and_events_work()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        var added = 0;
        var updated = 0;
        var removed = 0;
        chat.MessageAdded += (_, _) => added++;
        chat.MessageUpdated += (_, _) => updated++;
        chat.MessageRemoved += (_, _) => removed++;

        var first = new PuraChatMessage { Id = "m1", Role = PuraChatRole.User, Markdown = "one" };
        chat.AddMessage(first);
        Assert.Single(chat.Messages);

        first.Markdown = "one-updated";
        Assert.Equal(2, first.ContentVersion);
        Assert.Equal(PuraChatMessageStatus.Complete, first.Status);

        chat.InsertMessage(0, new PuraChatMessage { Id = "m0", Role = PuraChatRole.System, Markdown = "zero" });
        Assert.Equal("m0", chat.Messages[0].Id);
        Assert.True(chat.RemoveMessage("m1"));
        Assert.Null(chat.FindMessage("m1"));

        Assert.Equal(2, added);
        Assert.Equal(0, updated);
        Assert.Equal(1, removed);
    }

    [AvaloniaFact]
    public void Chat_snapshot_round_trips()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        chat.AddMessage(new PuraChatMessage
        {
            Id = "m1",
            Role = PuraChatRole.Assistant,
            Markdown = "hello **world**",
            Status = PuraChatMessageStatus.Complete,
        });
        chat.Messages[0].Metadata["trace"] = "abc";

        var json = PuraChatSnapshotSerializer.Serialize(chat.CreateSnapshot());
        var restored = new PuraChatView { Options = CreateOptions() };
        restored.RestoreSnapshot(PuraChatSnapshotSerializer.Deserialize(json));

        Assert.Single(restored.Messages);
        Assert.Equal("hello **world**", restored.Messages[0].Markdown);
        Assert.Equal("abc", restored.Messages[0].Metadata["trace"]);
    }

    [AvaloniaFact]
    public async Task Chat_streaming_updates_existing_message_target()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        var target = new PuraChatMessage { Id = "m1", Role = PuraChatRole.Assistant };
        var session = chat.BeginStreamingMessage(target);
        await session.AppendAsync("hello".AsMemory());
        await session.CompleteAsync();

        var message = Assert.Single(chat.Messages);
        Assert.Same(target, message);
        Assert.Equal("hello", message.Markdown);
        Assert.Equal(PuraChatMessageStatus.Complete, message.Status);
    }

    [AvaloniaFact]
    public async Task Chat_abort_keeps_partial_message()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        var session = chat.BeginStreamingMessage(new PuraChatMessage { Id = "m1", Role = PuraChatRole.Assistant });
        await session.AppendAsync("partial".AsMemory());
        chat.AbortStreaming();
        await session.AbortAsync();

        var message = Assert.Single(chat.Messages);
        Assert.Equal(PuraChatMessageStatus.Aborted, message.Status);
        Assert.Contains("partial", message.Markdown, StringComparison.Ordinal);
    }

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


