using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Mermaider;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Agent;

namespace PuraMarkdown.Extension.Diagrams.Mermaid;

/// <summary>
/// Mermaid 图表渲染器（真实引擎：Mermaider，MIT、纯 .NET、无浏览器/DOM）。
/// M3 使用其 ASCII 渲染作为显示形态（无额外 SVG 渲染依赖）；SVG 渲染留作后续可选能力。
/// </summary>
public sealed class MermaidDiagramRenderer : IFenceRenderer
{
    private readonly ConcurrentDictionary<ulong, (string Ascii, string? Error)> _cache = new();

    /// <summary>围栏渲染器名称。</summary>
    public string Name => "pura.mermaid";

    /// <summary>是否处理 mermaid 围栏。</summary>
    /// <param name="language">围栏语言。</param>
    /// <param name="source">围栏源码。</param>
    /// <param name="isClosed">围栏是否闭合。</param>
    /// <param name="context">渲染上下文。</param>
    public bool CanRender(string language, string source, bool isClosed, RenderContext context)
        => string.Equals(language, "mermaid", StringComparison.OrdinalIgnoreCase);

    /// <summary>渲染 Mermaid（ASCII）。</summary>
    /// <param name="language">围栏语言。</param>
    /// <param name="source">围栏源码。</param>
    /// <param name="isClosed">围栏是否闭合。</param>
    /// <param name="context">渲染上下文。</param>
    public Control Render(string language, string source, bool isClosed, RenderContext context)
    {
        var theme = context.Theme;

        // 流式尾部窗口：不调用引擎，避免每次追加重解析未完成的 Mermaid 源码。
        if (context.IsStreaming && context.IsTail)
        {
            var pending = new StackPanel { Spacing = 4 };
            pending.Children.Add(new TextBlock
            {
                Text = "Mermaid · Mermaider（生成中…）",
                FontSize = theme.Typography.BodySize - 2,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(theme.Colors.Accent),
            });
            pending.Children.Add(new TextBlock
            {
                Text = source,
                FontFamily = theme.Typography.MonoFont,
                FontSize = theme.Typography.BodySize - 2,
                Foreground = new SolidColorBrush(theme.Colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
            return new Border
            {
                BorderBrush = new SolidColorBrush(theme.Colors.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Background = new SolidColorBrush(theme.Colors.CodeBackground),
                Child = pending,
            };
        }

        string ascii;
        string? error;
        var key = Fnv1a(source);
        if (!_cache.TryGetValue(key, out var cached))
        {
            try
            {
                cached = (MermaidRenderer.RenderAscii(source), null);
            }
            catch (Exception ex) when (ex is MermaidParseException or MermaidSvgException or ArgumentException)
            {
                cached = (string.Empty, ex.Message);
            }

            _cache[key] = cached;
        }

        ascii = cached.Ascii;
        error = cached.Error;

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = "Mermaid · Mermaider",
            FontSize = theme.Typography.BodySize - 2,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(theme.Colors.Accent),
        });

        if (error is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"图表解析失败：{error}",
                FontSize = theme.Typography.BodySize - 1,
                Foreground = new SolidColorBrush(theme.Colors.Danger),
                TextWrapping = TextWrapping.Wrap,
            });
            stack.Children.Add(new TextBlock
            {
                Text = source,
                FontFamily = theme.Typography.MonoFont,
                FontSize = theme.Typography.BodySize - 2,
                Foreground = new SolidColorBrush(theme.Colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            stack.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = ascii,
                    FontFamily = theme.Typography.MonoFont,
                    FontSize = theme.Typography.BodySize - 2,
                    Foreground = new SolidColorBrush(theme.Colors.Text),
                    TextWrapping = TextWrapping.NoWrap,
                },
            });
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(theme.Colors.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            Child = stack,
        };
    }

    private static ulong Fnv1a(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}

/// <summary>MermaidPlugin。</summary>
public sealed class MermaidPlugin : IPuraPlugin
{
    /// <summary>Name。</summary>
    public string Name => "pura.diagrams.mermaid";

    /// <summary>Version。</summary>
    public string Version => "1.0.0";

    /// <summary>Description。</summary>
    public string? Description => "Mermaid 图表（Mermaider 纯 .NET 引擎）";

    /// <summary>Configure。</summary>
    /// <param name="builder">builder 参数。</param>
    public void Configure(PuraPipelineBuilder builder) => builder.UseFenceRenderer(new MermaidDiagramRenderer());
}




