using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Chat;

/// <summary>消息角色。</summary>
public enum PuraChatRole
{
    /// <summary>用户消息。</summary>
    User,
    /// <summary>助手消息。</summary>
    Assistant,
    /// <summary>系统消息。</summary>
    System,
}

/// <summary>消息状态。</summary>
public enum PuraChatMessageStatus
{
    /// <summary>已完成。</summary>
    Complete,
    /// <summary>流式中。</summary>
    Streaming,
    /// <summary>失败。</summary>
    Failed,
}

/// <summary>聊天消息模型。</summary>
public sealed class PuraChatMessage : INotifyPropertyChanged
{
    private string _markdown = string.Empty;
    private PuraChatMessageStatus _status;

    /// <summary>消息 ID。</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>角色。</summary>
    public PuraChatRole Role { get; init; }

    /// <summary>Markdown 内容。</summary>
    public string Markdown
    {
        get => _markdown;
        set => Set(ref _markdown, value);
    }

    /// <summary>消息状态。</summary>
    public PuraChatMessageStatus Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// 虚拟化聊天视图：历史消息每条一个 <see cref="PuraMarkdownView"/>，流式消息放在底部独立槽。
/// </summary>
public sealed class PuraChatView : ContentControl
{
    private readonly ObservableCollection<PuraChatMessage> _messages = [];
    private readonly ItemsRepeater _repeater;
    private readonly ScrollViewer _scroll;
    private readonly PuraMarkdownView _streamingView;
    private readonly Grid _root;
    private PuraStreamingSession? _session;

    /// <summary>创建聊天视图。</summary>
    public PuraChatView()
    {
        _repeater = new ItemsRepeater
        {
            ItemsSource = _messages,
            ItemTemplate = new FuncDataTemplate<PuraChatMessage>((message, _) => new PuraMarkdownView
            {
                Options = Options,
                Markdown = message.Markdown,
                Margin = new Thickness(16, 8),
            }),
        };
        _repeater.Layout = new StackLayout();

        _scroll = new ScrollViewer { Content = _repeater };
        _streamingView = new PuraMarkdownView
        {
            Margin = new Thickness(16, 8),
            IsVisible = false,
        };
        _streamingView.InteractionOccurred += (_, args) => InteractionOccurred?.Invoke(this, args);
        _streamingView.Warning += (_, args) => Warning?.Invoke(this, args);

        _root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(_scroll, 0);
        Grid.SetRow(_streamingView, 1);
        _root.Children.Add(_scroll);
        _root.Children.Add(_streamingView);
        Content = _root;
    }

    /// <summary>渲染配置；应在添加消息前设置。</summary>
    public PuraMarkdownOptions? Options { get; set; }

    /// <summary>历史消息（虚拟化）。</summary>
    public ObservableCollection<PuraChatMessage> Messages => _messages;

    /// <summary>交互事件（转发历史与流式消息）。</summary>
    public event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;

    /// <summary>警告事件。</summary>
    public event EventHandler<PuraWarningEventArgs>? Warning;

    /// <summary>追加一条已完成消息。</summary>
    public void AddMessage(PuraChatMessage message)
    {
        _messages.Add(message);
        ScrollToEnd();
    }

    /// <summary>开始流式追加一条新消息；完成后自动加入历史。</summary>
    /// <param name="message">可选的消息模板（Id/Role）。</param>
    public PuraStreamingSession BeginStreamingMessage(PuraChatMessage? message = null)
    {
        if (_session is { State: PuraStreamingState.Streaming })
        {
            throw new InvalidOperationException("已有进行中的流式消息");
        }

        _session = null;
        _streamingView.Options = Options;
        _streamingView.Markdown = null;
        _streamingView.IsVisible = true;
        _session = _streamingView.BeginStreaming();

        var target = message ?? new PuraChatMessage { Role = PuraChatRole.Assistant };
        var activeSession = _session;
        activeSession.Completed += (_, _) =>
        {
            _streamingView.IsVisible = false;
            var completed = new PuraChatMessage
            {
                Id = target.Id,
                Role = target.Role,
                Markdown = activeSession.CurrentText,
                Status = PuraChatMessageStatus.Complete,
            };
            _messages.Add(completed);
            _streamingView.Markdown = null;
            _session = null;
            ScrollToEnd();
        };
        _session.Aborted += (_, _) =>
        {
            _streamingView.IsVisible = false;
            _streamingView.Markdown = null;
            _session = null;
        };

        return _session;
    }

    /// <summary>滚动到底部（用户已阅读到底部时使用）。</summary>
    public void ScrollToEnd()
        => Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    /// <summary>按消息 ID 滚动（按索引估算；虚拟化容器不暴露精确 ScrollIntoView）。</summary>
    public void ScrollToMessage(string messageId)
    {
        var index = -1;
        for (var i = 0; i < _messages.Count; i++)
        {
            if (_messages[i].Id == messageId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var extent = _scroll.Extent.Height;
        if (extent > 0)
        {
            _scroll.Offset = new Vector(0, extent * index / Math.Max(1, _messages.Count));
        }
    }
}


