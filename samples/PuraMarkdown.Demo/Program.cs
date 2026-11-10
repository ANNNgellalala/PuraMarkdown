using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PuraMarkdown.Agent;
using PuraMarkdown.Extension.Diagrams.Mermaid;
using PuraMarkdown.Extension.Highlighting.ColorCode;
using PuraMarkdown.Plugins.Samples;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Demo;

internal sealed class DemoApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DemoWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => AppBuilder.Configure<DemoApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}

internal sealed class DemoWindow : Window
{
    private const string Sample = """
        # PuraMarkdown Demo

        这是 **Avalonia 只读 Markdown 控件**的示例：流式追加、交互组件、工具调用、插件（高亮 / Mermaid / rating）。

        ## 代码

        ```csharp
        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji()
            .UseMathematics()
            .UseAgentComponents()
            .UsePlugin(new ColorCodePlugin())
            .UsePlugin(new MermaidPlugin())
            .UsePlugin(new CSharpMathMathPlugin())
            .Build();
        ```

        ## Emoji 与数学

        短代码：:smile: :fire:；行内公式：$E=mc^2$。

        $$
        \int_0^1 x^2 dx = \frac{1}{3}
        $$

        ## 图表

        ```mermaid
        graph TD
          A[Markdig] --> B[Renderer Registry]
          B --> C[Avalonia Controls]
          B --> D[Streaming]
        ```

        ## 需要你确认

        ```pura-agent
        {
          "type": "choice",
          "version": "1.0",
          "id": "next",
          "question": "下一步做什么？",
          "options": [
            { "id": "continue", "label": "继续 M4" },
            { "id": "review", "label": "先评审文档" }
          ]
        }
        ```

        ```pura-agent
        { "type": "rating", "version": "1.0", "id": "quality", "label": "本次输出质量", "max": 5, "value": 4 }
        ```

        ## 工具调用

        ```pura-tool
        { "id": "call_1", "name": "web_search", "status": "success",
          "arguments": { "q": "avalonia markdown" },
          "result": { "hits": 12, "top": "PuraMarkdown" },
          "durationMs": 1200 }
        ```

        ```pura-tool
        { "id": "call_2", "name": "db_query", "status": "running",
          "arguments": { "sql": "select * from invoices" } }
        ```

        ```pura-tool-call
        { "id": "call_3", "name": "send_email", "status": "error",
          "arguments": { "to": "ops@example.com" }, "error": "SMTP 超时" }
        ```

        ```pura-agent
        {
          "type": "tool-call",
          "version": "1.0",
          "id": "tool-1",
          "name": "file_read",
          "status": "success",
          "arguments": { "path": "/etc/hosts" },
          "result": "127.0.0.1 localhost"
        }
        ```

        感谢使用 PuraMarkdown。
        """;

    private const string Warmup = """
        | A | B |
        | --- | --- |
        | 1 | 2 |

        ```csharp
        var x = 1;
        ```

        ```mermaid
        graph LR
          A --> B
        ```

        ```pura-agent
        { "type": "choice", "version": "1.0", "id": "w", "question": "W", "options": [ { "id": "a", "label": "A" } ] }
        ```

        ```pura-agent
        { "type": "rating", "version": "1.0", "id": "wr", "label": "W", "max": 5, "value": 3 }
        ```
        """;

    private readonly PuraMarkdownView _view;
    private readonly TextBlock _log = new() { Text = "ready", Foreground = Brushes.Gray };
    private readonly TextBlock _status = new() { Text = "就绪", Foreground = Brushes.Gray };
    private readonly Button _startButton;
    private readonly Button _resetButton;
    private readonly Button _themeButton;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly int _chunkSize = 16;
    private int _offset;
    private PuraStreamingSession? _session;
    private bool _dark;

    public DemoWindow()
    {
        Title = "PuraMarkdown Demo";
        Width = 980;
        Height = 760;

        var pipeline = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseEmoji()
            .UseMathematics()
            .UsePlugin(new ColorCodePlugin())
            .UsePlugin(new MermaidPlugin())
            .UsePlugin(new CSharpMathMathPlugin())
            .UsePlugin(new RatingPlugin())
            .Build();

        _view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions
            {
                Pipeline = pipeline,
                Theme = PuraTheme.Light,
            },
        };
        _view.InteractionOccurred += OnInteraction;
        _view.Announced += (_, message) => _status.Text = message;

        _startButton = new Button { Content = "开始流式" };
        _startButton.Click += (_, _) => StartStreaming();
        _resetButton = new Button { Content = "重置" };
        _resetButton.Click += (_, _) => Reset();
        _themeButton = new Button { Content = "切换深色" };
        _themeButton.Click += (_, _) => ToggleTheme();
        var longChatButton = new Button { Content = "长对话 (1000)" };
        longChatButton.Click += (_, _) => new VirtualizedChatWindow(_view.Options!).Show();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 8),
            Children = { _startButton, _resetButton, _themeButton, longChatButton, _status },
        };

        var content = new ScrollViewer
        {
            Content = _view,
            Padding = new Thickness(16),
        };

        var logPanel = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6),
            Child = _log,
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(content, 1);
        Grid.SetRow(logPanel, 2);
        grid.Children.Add(toolbar);
        grid.Children.Add(content);
        grid.Children.Add(logPanel);
        Content = grid;

        Opened += (_, _) => _view.Prewarm(Warmup);
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _timer.Tick += (_, _) => Tick();
    }

    private void StartStreaming()
    {
        Reset();
        _session = _view.BeginStreaming();
        _timer.Start();
        _status.Text = "流式中…";
    }

    private void Tick()
    {
        if (_session is null)
        {
            return;
        }

        if (_offset >= Sample.Length)
        {
            _timer.Stop();
            _ = _session.CompleteAsync();
            _status.Text = "完成";
            return;
        }

        var length = Math.Min(_chunkSize, Sample.Length - _offset);
        _session.Append(Sample.Substring(_offset, length));
        _offset += length;
    }

    private void Reset()
    {
        _timer.Stop();
        if (_session is { State: PuraStreamingState.Streaming })
        {
            _ = _session.AbortAsync();
        }

        _session = null;
        _offset = 0;
        _view.Markdown = null;
        _status.Text = "就绪";
        _log.Text = "reset";
    }

    private void ToggleTheme()
    {
        _dark = !_dark;
        _themeButton.Content = _dark ? "切换浅色" : "切换深色";
        _view.Options = new PuraMarkdownOptions
        {
            Pipeline = _view.Options!.Pipeline,
            Theme = _dark ? PuraTheme.Dark : PuraTheme.Light,
            FollowSystemTheme = false,
        };
        _view.Render();
    }

    private void OnInteraction(object? sender, PuraInteractionEventArgs args)
    {
        _log.Text = args.ComponentType switch
        {
            "choice" => $"choice：{string.Join(", ", args.SelectedIds)}",
            "rating" => $"rating：{string.Join(", ", args.SelectedIds)} 星",
            "confirm" => $"confirm：{args.Confirmed}",
            _ => $"{args.ComponentType} 事件",
        };
    }
}




