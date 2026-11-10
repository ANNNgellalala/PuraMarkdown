using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Agent.Rendering;

internal sealed class CodeBlockRenderer : IIncrementalPuraRenderer
{
    private readonly ConcurrentDictionary<(string Language, ulong Hash), IReadOnlyList<HighlightSpan>> _highlightCache = new();

    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is CodeBlock;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var codeBlock = (CodeBlock)node;
        var language = GetLanguage(codeBlock);
        var code = MarkdownText.GetLeafText(codeBlock).TrimEnd('\n');
        var originalCode = code;
        var maxFence = context.Options.Limits.MaxFenceChars;
        if (code.Length > maxFence)
        {
            context.Warn("PURA-LIM-002", $"代码围栏超过 {maxFence} 字符，已截断显示");
            code = code[..maxFence] + "\n…[代码已截断]";
        }

        var theme = context.Theme;
        var localizer = context.Options.Localizer;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var languageLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(language) ? localizer.Get(PuraStringKeys.CodeLanguageFallback) : language,
            FontSize = theme.Typography.BodySize - 2,
            Foreground = new SolidColorBrush(theme.Colors.Muted),
            FontFamily = theme.Typography.MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(languageLabel, 0);
        header.Children.Add(languageLabel);

        var copyButton = new Button
        {
            Content = localizer.Get(PuraStringKeys.Copy),
            FontSize = theme.Typography.BodySize - 3,
            Padding = new Thickness(8, 2),
        };
        copyButton.Click += async (_, _) =>
        {
            try
            {
                await context.Options.Clipboard.SetTextAsync(originalCode);
                copyButton.Content = localizer.Get(PuraStringKeys.Copied);
            }
            catch
            {
                copyButton.Content = localizer.Get(PuraStringKeys.CopyFailed);
            }
        };
        AutomationProperties.SetName(copyButton, localizer.Get(PuraStringKeys.CopyCodeAutomation));
        Grid.SetColumn(copyButton, 1);
        header.Children.Add(copyButton);

        var codeText = PuraTextBlockFactory.Create(context);
        codeText.TextWrapping = TextWrapping.NoWrap;
        codeText.FontFamily = theme.Typography.MonoFont;
        codeText.FontSize = theme.Typography.BodySize - 1;
        codeText.Foreground = new SolidColorBrush(theme.Colors.Text);

        if (context.IsStreaming && code.Length > 16 * 1024)
        {
            // 超长代码块流式期间只显示纯文本，避免 UI 线程生成过多 Run；Complete 后一次性高亮。
            codeText.Text = code;
        }
        else if (language is not null && context.Highlighter.CanHighlight(language))
        {
            try
            {
                if (context.PrecomputedHighlights is not null
                    && context.PrecomputedHighlights.TryGetValue(codeBlock, out var spans))
                {
                    AppendHighlighted(codeText, code, spans, context);
                }
                else
                {
                    AppendHighlighted(codeText, code, language, context);
                }
            }
            catch (Exception ex)
            {
                context.Warn("PURA-RENDER-002", $"高亮失败，回退纯文本：{ex.Message}", codeBlock.GetType().Name);
                codeText.Inlines?.Clear();
                codeText.Text = code;
            }
        }
        else
        {
            codeText.Text = code;
        }

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = codeText,
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(header);
        stack.Children.Add(body);

        return new Border
        {
            Background = new SolidColorBrush(theme.Colors.CodeBackground),
            BorderBrush = new SolidColorBrush(theme.Colors.CodeBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(theme.Spacing.CodePadding),
            Margin = new Thickness(0, 0, 0, theme.Spacing.CodeBlockSpacing),
            Child = stack,
        };
    }

    internal static string? GetLanguage(CodeBlock codeBlock)
    {
        if (codeBlock is not FencedCodeBlock fenced || string.IsNullOrWhiteSpace(fenced.Info))
        {
            return null;
        }

        var info = fenced.Info.Trim();
        var space = info.IndexOf(' ');
        return space < 0 ? info : info[..space];
    }

    /// <summary>CanUpdate。</summary>
    /// <param name="oldNode">oldNode 参数。</param>
    /// <param name="newNode">newNode 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context)
        => oldNode is CodeBlock oldCode
           && newNode is CodeBlock newCode
           && GetLanguage(oldCode) == GetLanguage(newCode);

    /// <summary>Update。</summary>
    /// <param name="existing">existing 参数。</param>
    /// <param name="newNode">newNode 参数。</param>
    /// <param name="context">context 参数。</param>
    public void Update(Control existing, MarkdownObject newNode, RenderContext context)
    {
        if (existing.Tag is not TextBlock codeText)
        {
            return;
        }

        var codeBlock = (CodeBlock)newNode;
        var code = MarkdownText.GetLeafText(codeBlock).TrimEnd('\n');
        var language = GetLanguage(codeBlock);
        codeText.Inlines?.Clear();

        if (context.IsStreaming && code.Length > 16 * 1024)
        {
            codeText.Text = code;
        }
        else if (language is not null && context.Highlighter.CanHighlight(language))
        {
            if (context.PrecomputedHighlights is not null
                && context.PrecomputedHighlights.TryGetValue(newNode, out var spans))
            {
                AppendHighlighted(codeText, code, spans, context);
            }
            else
            {
                AppendHighlighted(codeText, code, language, context);
            }
        }
        else
        {
            codeText.Text = code;
        }
    }

    private void AppendHighlighted(TextBlock textBlock, string code, string language, RenderContext context)
    {
        var hash = Fnv1a(code);
        var spans = _highlightCache.GetOrAdd(
            (language, hash),
            _ => context.Highlighter.Highlight(code, language)
                .Where(s => s.Start >= 0 && s.Length > 0 && s.Start < code.Length)
                .OrderBy(s => s.Start)
                .ToArray());

        AppendHighlighted(textBlock, code, spans, context);
    }

    private static void AppendHighlighted(
        TextBlock textBlock,
        string code,
        IReadOnlyList<HighlightSpan> spans,
        RenderContext context)
    {

        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start < cursor)
            {
                continue;
            }

            if (span.Start > cursor)
            {
                textBlock.Inlines!.Add(new Avalonia.Controls.Documents.Run(code[cursor..span.Start]));
            }

            var length = Math.Min(span.Length, code.Length - span.Start);
            textBlock.Inlines!.Add(new Avalonia.Controls.Documents.Run(code.Substring(span.Start, length))
            {
                Foreground = new SolidColorBrush(context.Theme.CodeTheme.Resolve(span.Token)),
            });
            cursor = span.Start + length;
        }

        if (cursor < code.Length)
        {
            textBlock.Inlines!.Add(new Avalonia.Controls.Documents.Run(code[cursor..]));
        }
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

internal sealed class TableRenderer : IPuraRenderer
{
    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context) => node is Table;

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var table = (Table)node;
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columns == 0)
        {
            return new TextBlock { Text = "[空表格]" };
        }

        var grid = new Grid();
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[r];
            var cells = row.OfType<TableCell>().ToList();
            for (var c = 0; c < columns; c++)
            {
                var cell = c < cells.Count ? cells[c] : null;
                var cellPanel = new StackPanel { Spacing = 0 };
                if (cell is not null)
                {
                    foreach (var child in cell)
                    {
                        cellPanel.Children.Add(context.RenderMarkdown(child));
                    }
                }

                var alignment = c < table.ColumnDefinitions.Count
                    ? table.ColumnDefinitions[c].Alignment
                    : null;
                if (alignment is not null)
                {
                    ApplyTextAlignment(cellPanel, alignment.Value);
                }

                var cellBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(context.Theme.Colors.Border),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4),
                    Child = cellPanel,
                };
                if (row.IsHeader)
                {
                    cellBorder.Background = new SolidColorBrush(context.Theme.Colors.CodeBackground);
                }

                Grid.SetColumn(cellBorder, c);
                Grid.SetRow(cellBorder, r);
                grid.Children.Add(cellBorder);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, context.Theme.Spacing.BlockSpacing),
            Content = grid,
        };
    }

    private static void ApplyTextAlignment(Control control, TableColumnAlign alignment)
    {
        var textAlignment = alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };

        switch (control)
        {
            case TextBlock textBlock:
                textBlock.TextAlignment = textAlignment;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    ApplyTextAlignment(child, alignment);
                }

                break;
            case ContentControl content when content.Content is Control childControl:
                ApplyTextAlignment(childControl, alignment);
                break;
            case Border border when border.Child is Control borderChild:
                ApplyTextAlignment(borderChild, alignment);
                break;
        }
    }
}

/// <summary>图表围栏路由：按注册顺序询问 IDiagramRenderer；失败回退代码块。</summary>
internal sealed class FenceDispatchRenderer : IPuraRenderer
{
    private readonly IReadOnlyList<IFenceRenderer> _renderers;

    public FenceDispatchRenderer(IReadOnlyList<IFenceRenderer> renderers)
    {
        _renderers = renderers;
    }

    /// <summary>CanRender。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public bool CanRender(MarkdownObject node, RenderContext context)
    {
        if (node is not FencedCodeBlock fenced || _renderers.Count == 0)
        {
            return false;
        }

        var language = CodeBlockRenderer.GetLanguage(fenced);
        if (string.IsNullOrEmpty(language))
        {
            return false;
        }

        var source = MarkdownText.GetLeafText(fenced);
        var isClosed = fenced.ClosingFencedCharCount > 0;
        return _renderers.Any(renderer => SafeCanRender(renderer, language, source, isClosed, context));
    }

    /// <summary>Render。</summary>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public Control Render(MarkdownObject node, RenderContext context)
    {
        var fenced = (FencedCodeBlock)node;
        var language = CodeBlockRenderer.GetLanguage(fenced) ?? string.Empty;
        var source = MarkdownText.GetLeafText(fenced);
        var isClosed = fenced.ClosingFencedCharCount > 0;

        foreach (var renderer in _renderers)
        {
            if (!SafeCanRender(renderer, language, source, isClosed, context))
            {
                continue;
            }

            try
            {
                return renderer.Render(language, source, isClosed, context);
            }
            catch (Exception ex)
            {
                context.Warn("PURA-RENDER-003", $"围栏渲染失败，回退代码块：{ex.Message}", fenced.GetType().Name, renderer.Name);
                break;
            }
        }

        return new CodeBlockRenderer().Render(node, context);
    }

    private static bool SafeCanRender(IFenceRenderer renderer, string language, string source, bool isClosed, RenderContext context)
    {
        try
        {
            return renderer.CanRender(language, source, isClosed, context);
        }
        catch
        {
            return false;
        }
    }
}











