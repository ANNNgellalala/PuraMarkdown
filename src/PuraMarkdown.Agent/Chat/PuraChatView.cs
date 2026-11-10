using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Agent.Chat;

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
    /// <summary>被中断（保留已生成内容）。</summary>
    Aborted,
}

/// <summary>自动滚动策略。</summary>
public enum PuraChatAutoScrollMode
{
    /// <summary>不自动滚动。</summary>
    Never,
    /// <summary>仅当用户接近底部时滚动。</summary>
    NearBottom,
    /// <summary>始终滚动到底部。</summary>
    Always,
}

/// <summary>聊天消息模型。</summary>
public sealed class PuraChatMessage : INotifyPropertyChanged
{
    private string _markdown = string.Empty;
    private PuraChatMessageStatus _status;
    private string? _error;

    /// <summary>消息 ID。</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>角色。</summary>
    public PuraChatRole Role { get; init; }

    /// <summary>Markdown 内容。</summary>
    public string Markdown
    {
        get => _markdown;
        set
        {
            if (Set(ref _markdown, value))
            {
                ContentVersion++;
                UpdatedAt = DateTimeOffset.Now;
            }
        }
    }

    /// <summary>消息状态。</summary>
    public PuraChatMessageStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                UpdatedAt = DateTimeOffset.Now;
            }
        }
    }

    /// <summary>失败/中断原因。</summary>
    public string? Error
    {
        get => _error;
        set => Set(ref _error, value);
    }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; } = DateTimeOffset.Now;

    /// <summary>内容版本（每次 Markdown 变化递增，便于宿主做增量同步）。</summary>
    public int ContentVersion { get; private set; }

    /// <summary>宿主自定义元数据（工具调用 ID、引用、token 统计等）。</summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>消息快照（用于持久化/恢复）。</summary>
public sealed record PuraChatMessageSnapshot(
    string Id,
    PuraChatRole Role,
    string Markdown,
    PuraChatMessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Error,
    Dictionary<string, string>? Metadata);

/// <summary>整段会话快照。</summary>
public sealed record PuraChatSnapshot(int Version, IReadOnlyList<PuraChatMessageSnapshot> Messages);

/// <summary>聊天快照序列化。</summary>
public static class PuraChatSnapshotSerializer
{
    /// <summary>序列化为 JSON。</summary>
    public static string Serialize(PuraChatSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });

    /// <summary>从 JSON 恢复。</summary>
    public static PuraChatSnapshot Deserialize(string json)
        => JsonSerializer.Deserialize<PuraChatSnapshot>(json)
           ?? throw new JsonException("聊天快照为空");
}

/// <summary>
/// 虚拟化聊天视图：历史消息每条一个 <see cref="PuraMarkdownView"/>，流式消息放在底部独立槽。
/// 本包只提供数据/生命周期能力，不定义新的消息气泡等 UI 组件。
/// </summary>
public sealed class PuraChatView : ContentControl
{
    private readonly ObservableCollection<PuraChatMessage> _messages = [];
    private readonly ItemsRepeater _repeater;
    private readonly ScrollViewer _scroll;
    private readonly PuraMarkdownView _streamingView;
    private readonly Grid _root;
    private PuraStreamingSession? _session;
    private PuraMarkdownOptions? _options;

    /// <summary>创建聊天视图。</summary>
    public PuraChatView()
    {
        _repeater = new ItemsRepeater
        {
            ItemsSource = _messages,
            ItemTemplate = new FuncDataTemplate<PuraChatMessage>((message, _) => CreateMessageView(message)),
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

        _messages.CollectionChanged += OnMessagesChanged;
    }

    /// <summary>渲染配置；设置后会重建已实现的消息视图。</summary>
    public PuraMarkdownOptions? Options
    {
        get => _options;
        set
        {
            if (ReferenceEquals(_options, value))
            {
                return;
            }

            _options = value;
            _streamingView.Options = value;
            RecreateMessageViews();
        }
    }

    /// <summary>自动滚动策略，默认接近底部时跟随。</summary>
    public PuraChatAutoScrollMode AutoScrollMode { get; set; } = PuraChatAutoScrollMode.NearBottom;

    /// <summary>历史消息（虚拟化容器数据源）。</summary>
    public ObservableCollection<PuraChatMessage> Messages => _messages;

    /// <summary>当前活动流式会话。</summary>
    public PuraStreamingSession? ActiveSession => _session;

    /// <summary>是否正在流式生成消息。</summary>
    public bool IsStreaming => _session?.State == PuraStreamingState.Streaming;

    /// <summary>是否接近底部（用于宿主决定是否跟随）。</summary>
    public bool IsNearBottom
        => _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - 48;

    /// <summary>写入了消息。</summary>
    public event EventHandler<PuraChatMessage>? MessageAdded;

    /// <summary>消息被更新（同 ID 替换或属性变化）。</summary>
    public event EventHandler<PuraChatMessage>? MessageUpdated;

    /// <summary>消息被移除。</summary>
    public event EventHandler<PuraChatMessage>? MessageRemoved;

    /// <summary>交互事件（转发历史与流式消息）。</summary>
    public event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;

    /// <summary>警告事件。</summary>
    public event EventHandler<PuraWarningEventArgs>? Warning;

    /// <summary>添加一条消息（同 ID 已存在时替换）。</summary>
    public void AddMessage(PuraChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var follow = ShouldFollow();
        Upsert(message);
        if (follow)
        {
            ScrollToEnd();
        }
    }

    /// <summary>在指定位置插入一条消息。</summary>
    public void InsertMessage(int index, PuraChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var follow = ShouldFollow();
        var existing = FindMessage(message.Id);
        if (existing is not null)
        {
            _messages.Remove(existing);
        }

        _messages.Insert(Math.Clamp(index, 0, _messages.Count), message);
        MessageAdded?.Invoke(this, message);
        if (follow)
        {
            ScrollToEnd();
        }
    }

    /// <summary>按 ID 更新消息（不存在时添加）。</summary>
    public void UpdateMessage(PuraChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var index = IndexOf(message.Id);
        if (index < 0)
        {
            AddMessage(message);
            return;
        }

        if (!ReferenceEquals(_messages[index], message))
        {
            _messages[index] = message;
        }

        MessageUpdated?.Invoke(this, message);
    }

    /// <summary>按 ID 移除消息。</summary>
    public bool RemoveMessage(string messageId)
    {
        var index = IndexOf(messageId);
        if (index < 0)
        {
            return false;
        }

        var message = _messages[index];
        _messages.RemoveAt(index);
        MessageRemoved?.Invoke(this, message);
        return true;
    }

    /// <summary>清空历史消息（不结束流式会话）。</summary>
    public void ClearMessages()
    {
        var removed = _messages.ToArray();
        _messages.Clear();
        foreach (var message in removed)
        {
            MessageRemoved?.Invoke(this, message);
        }
    }

    /// <summary>替换全部历史消息。</summary>
    public void ReplaceMessages(IEnumerable<PuraChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ClearMessages();
        foreach (var message in messages)
        {
            AddMessage(message);
        }
    }

    /// <summary>按 ID 查找消息。</summary>
    public PuraChatMessage? FindMessage(string messageId)
        => IndexOf(messageId) is var index and >= 0 ? _messages[index] : null;

    /// <summary>开始流式生成一条新消息；完成后写回历史。</summary>
    /// <param name="message">可选消息模板（Id/Role）；同 ID 已存在时更新原对象。</param>
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
        target.Status = PuraChatMessageStatus.Streaming;
        var activeSession = _session;

        activeSession.Completed += (_, _) =>
        {
            _streamingView.IsVisible = false;
            target.Markdown = activeSession.CurrentText;
            target.Status = activeSession.LastWorkerError is null
                ? PuraChatMessageStatus.Complete
                : PuraChatMessageStatus.Failed;
            target.Error = activeSession.LastWorkerError?.Message;
            Upsert(target);
            _streamingView.Markdown = null;
            _session = null;
            if (ShouldFollow())
            {
                ScrollToEnd();
            }
        };
        activeSession.Aborted += (_, _) =>
        {
            _streamingView.IsVisible = false;
            target.Markdown = activeSession.CurrentText;
            target.Status = PuraChatMessageStatus.Aborted;
            target.Error = "流式生成已中断";
            Upsert(target);
            _streamingView.Markdown = null;
            _session = null;
        };

        return _session;
    }

    /// <summary>中断当前流式生成（保留部分内容并写入历史）。</summary>
    public void AbortStreaming()
    {
        if (_session is { State: PuraStreamingState.Streaming } session)
        {
            _ = session.AbortAsync();
        }
    }

    /// <summary>滚动到底部。</summary>
    public void ScrollToEnd()
        => Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);

    /// <summary>按消息 ID 滚动；已实现则精确 BringIntoView，否则按索引估算。</summary>
    public void ScrollToMessage(string messageId)
    {
        var index = IndexOf(messageId);
        if (index < 0)
        {
            return;
        }

        var realized = _repeater.GetVisualDescendants()
            .OfType<PuraMarkdownView>()
            .FirstOrDefault(view => Equals(view.Tag, messageId));
        if (realized is not null)
        {
            realized.BringIntoView();
            return;
        }

        var extent = _scroll.Extent.Height;
        if (extent > 0)
        {
            _scroll.Offset = new Vector(0, extent * index / Math.Max(1, _messages.Count));
        }
    }

    /// <summary>导出会话快照（不含 UI/渲染器状态）。</summary>
    public PuraChatSnapshot CreateSnapshot()
        => new(
            1,
            _messages.Select(message => new PuraChatMessageSnapshot(
                message.Id,
                message.Role,
                message.Markdown,
                message.Status,
                message.CreatedAt,
                message.UpdatedAt,
                message.Error,
                new Dictionary<string, string>(message.Metadata))).ToArray());

    /// <summary>从快照恢复消息（当前流式会话会保持）。</summary>
    public void RestoreSnapshot(PuraChatSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ClearMessages();
        foreach (var item in snapshot.Messages)
        {
            var message = new PuraChatMessage
            {
                Id = item.Id,
                Role = item.Role,
                Markdown = item.Markdown,
                Status = item.Status,
                Error = item.Error,
                CreatedAt = item.CreatedAt,
            };
            message.UpdatedAt = item.UpdatedAt;
            if (item.Metadata is not null)
            {
                foreach (var pair in item.Metadata)
                {
                    message.Metadata[pair.Key] = pair.Value;
                }
            }

            AddMessage(message);
        }
    }

    private PuraMarkdownView CreateMessageView(PuraChatMessage message)
    {
        var view = new PuraMarkdownView
        {
            Options = Options,
            Markdown = message.Markdown,
            Margin = new Thickness(16, 8),
            Tag = message.Id,
        };
        view.InteractionOccurred += (_, args) => InteractionOccurred?.Invoke(this, args);
        view.Warning += (_, args) => Warning?.Invoke(this, args);

        void OnMessageChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(PuraChatMessage.Markdown))
            {
                return;
            }

            Dispatcher.UIThread.Post(() => view.Markdown = message.Markdown, DispatcherPriority.Background);
        }

        void Attach()
        {
            message.PropertyChanged -= OnMessageChanged;
            message.PropertyChanged += OnMessageChanged;
        }
        void Detach() => message.PropertyChanged -= OnMessageChanged;

        view.AttachedToVisualTree += (_, _) => Attach();
        view.DetachedFromVisualTree += (_, _) => Detach();
        Attach();
        return view;
    }

    private void Upsert(PuraChatMessage message)
    {
        var index = IndexOf(message.Id);
        if (index >= 0)
        {
            if (!ReferenceEquals(_messages[index], message))
            {
                _messages[index] = message;
            }

            MessageUpdated?.Invoke(this, message);
            return;
        }

        _messages.Add(message);
        MessageAdded?.Invoke(this, message);
    }

    private int IndexOf(string messageId)
    {
        for (var i = 0; i < _messages.Count; i++)
        {
            if (string.Equals(_messages[i].Id, messageId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private bool ShouldFollow()
        => AutoScrollMode switch
        {
            PuraChatAutoScrollMode.Always => true,
            PuraChatAutoScrollMode.NearBottom => IsNearBottom,
            _ => false,
        };

    private void RecreateMessageViews()
    {
        if (_repeater is null)
        {
            return;
        }

        _repeater.ItemsSource = null;
        _repeater.ItemsSource = _messages;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        // 集合外部修改时仍保证新增消息可自动跟随。
        if (args.Action == NotifyCollectionChangedAction.Add && ShouldFollow())
        {
            ScrollToEnd();
        }
    }
}



