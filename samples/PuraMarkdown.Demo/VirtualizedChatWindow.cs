using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PuraMarkdown.Demo;

/// <summary>
/// 长对话参考实现：历史消息用 ItemsRepeater 虚拟化，只实例化视口内的消息控件；
/// 新消息追加后自动滚动到底部。宿主可据此替换自己的聊天列表容器。
/// </summary>
internal sealed class VirtualizedChatWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private readonly ItemsRepeater _repeater;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(8, 0, 0, 0) };
    private readonly PuraMarkdownOptions _options;
    private readonly DispatcherTimer _realizedTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public VirtualizedChatWindow(PuraMarkdownOptions options)
    {
        _options = options;
        Title = "PuraMarkdown · 虚拟化长对话（1000 条）";
        Width = 980;
        Height = 760;

        _repeater = new ItemsRepeater
        {
            ItemsSource = _messages,
            ItemTemplate = new FuncDataTemplate<ChatMessage>((message, _) => new PuraMarkdownView
            {
                Options = _options,
                Markdown = message.Markdown,
                Margin = new Thickness(16, 8),
            }),
        };
        _repeater.Layout = new StackLayout();

        _scroll = new ScrollViewer { Content = _repeater };
        var addButton = new Button { Content = "追加 200 条" };
        addButton.Click += (_, _) => AppendMessages(200);
        var clearButton = new Button { Content = "清空" };
        clearButton.Click += (_, _) =>
        {
            _messages.Clear();
            UpdateStatus();
        };
        var topButton = new Button { Content = "回到顶部" };
        topButton.Click += (_, _) => _scroll.Offset = new Vector(0, 0);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8),
            Children = { addButton, clearButton, topButton, _status },
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(_scroll);
        Content = grid;

        _realizedTimer.Tick += (_, _) => UpdateStatus();
        _realizedTimer.Start();

        AppendMessages(1000);
    }

    private void AppendMessages(int count)
    {
        var start = _messages.Count;
        for (var i = 0; i < count; i++)
        {
            _messages.Add(new ChatMessage(MessageMarkdown(start + i)));
        }

        Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var realizedViews = this.GetVisualDescendants().OfType<PuraMarkdownView>().Count();
        _status.Text = $"messages={_messages.Count}  realizedViews={realizedViews}";
    }

    private static string MessageMarkdown(int index) => $"""
        ## Message {index}

        This is message {index} with **bold**, `code` and a list.

        - item a
        - item b

        | A | B |
        | --- | --- |
        | {index} | ok |
        """;

    private sealed record ChatMessage(string Markdown);
}

