using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using PuraMarkdown.Platform;
using PuraMarkdown.Security;
using PuraMarkdown.Rendering;

using MdInline = Markdig.Syntax.Inlines.Inline;

namespace PuraMarkdown.Rendering;

/// <summary>默认行内渲染：把 Markdig inline 组合为 Avalonia InlineCollection。</summary>
internal static class DefaultInlineRenderer
{
    /// <summary>RenderChildren。</summary>
    /// <param name="container">container 参数。</param>
    /// <param name="target">target 参数。</param>
    /// <param name="context">context 参数。</param>
    /// <param name="skipFirst">skipFirst 参数。</param>
    public static void RenderChildren(ContainerInline? container, InlineCollection target, RenderContext context, bool skipFirst = false)
    {
        if (container is null)
        {
            return;
        }

        var isFirst = true;
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            if (skipFirst && isFirst)
            {
                isFirst = false;
                continue;
            }

            isFirst = false;
            RenderOne(inline, target, context);
        }
    }

    /// <summary>RenderOne。</summary>
    /// <param name="inline">inline 参数。</param>
    /// <param name="target">target 参数。</param>
    /// <param name="context">context 参数。</param>
    public static void RenderOne(MdInline inline, InlineCollection target, RenderContext context)
    {
        var theme = context.Theme;

        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run(literal.Content.ToString()));
                return;

            case CodeInline code:
                target.Add(new Run(code.Content)
                {
                    FontFamily = theme.Typography.MonoFont,
                    Background = new SolidColorBrush(theme.Colors.CodeBackground),
                });
                return;

            case LineBreakInline lineBreak:
                // CommonMark：软换行是空格，硬换行（行尾两空格/反斜杠）才产生 <br/>。
                if (lineBreak.IsHard)
                {
                    target.Add(new LineBreak());
                }
                else
                {
                    target.Add(new Run(" "));
                }

                return;

            case EmphasisInline emphasis:
                RenderEmphasis(emphasis, target, context);
                return;

            case LinkInline link:
                RenderLink(link, target, context);
                return;

            case AutolinkInline autolink:
                target.Add(new Run(autolink.Url ?? string.Empty)
                {
                    Foreground = new SolidColorBrush(theme.Colors.Accent),
                    TextDecorations = TextDecorations.Underline,
                });
                return;

            case TaskList taskList:
                target.Add(new Run(taskList.Checked ? "☑ " : "☐ "));
                return;

            default:
                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    target.Add(new Run(text));
                }
                return;
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, InlineCollection target, RenderContext context)
    {
        var temp = new InlineCollection();
        RenderChildren(emphasis, temp, context);

        var isStrike = emphasis.DelimiterChar == '~';
        var isStrong = emphasis.DelimiterCount >= 2;
        var isItalic = !isStrike && emphasis.DelimiterCount % 2 == 1;

        foreach (var child in temp.ToList())
        {
            if (child is Run run)
            {
                if (isStrike)
                {
                    run.TextDecorations = TextDecorations.Strikethrough;
                }
                else
                {
                    if (isStrong)
                    {
                        run.FontWeight = FontWeight.Bold;
                    }
                    if (isItalic)
                    {
                        run.FontStyle = FontStyle.Italic;
                    }
                }
            }

            target.Add(child);
        }
    }

    private static void RenderLink(LinkInline link, InlineCollection target, RenderContext context)
    {
        var theme = context.Theme;
        var blocked = context.Sanitized.IsBlocked(link);
        var url = link.Url ?? string.Empty;

        if (link.IsImage)
        {
            var alt = GetInlineText(link) is { Length: > 0 } altText ? altText : "图片";
            var placeholder = new TextBlock
            {
                Text = $"[图片: {alt}]",
                Foreground = new SolidColorBrush(theme.Colors.Muted),
                FontStyle = FontStyle.Italic,
                FontSize = theme.Typography.BodySize,
            };
            var image = new Image { MaxWidth = 320, MaxHeight = 240, IsVisible = false };
            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(placeholder);
            panel.Children.Add(image);
            target.Add(new InlineUIContainer(panel));

            if (!blocked && !string.IsNullOrWhiteSpace(url))
            {
                _ = LoadImageAsync(context, url, image, placeholder);
            }
            else if (blocked)
            {
                placeholder.Text = $"[图片被拒绝: {url}]";
            }

            return;
        }

        var linkText = GetInlineText(link) is { Length: > 0 } text ? text : url;
        if (blocked)
        {
            target.Add(new Run(linkText)
            {
                Foreground = new SolidColorBrush(theme.Colors.Muted),
            });
            return;
        }

        var hyperlink = new HyperlinkButton
        {
            Content = linkText,
            Padding = new Thickness(0),
            FontSize = theme.Typography.BodySize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hyperlink.Click += async (_, _) =>
        {
            try
            {
                await context.Options.LinkHandler.OpenAsync(url);
            }
            catch (Exception ex)
            {
                context.Warn("PURA-LINK-001", $"打开链接失败：{ex.Message}", nameof(LinkInline));
            }
        };

        target.Add(new InlineUIContainer(hyperlink));
    }

    private static HyperlinkButton CreateHyperlink(string text, string url, RenderContext context)
    {
        var hyperlink = new HyperlinkButton
        {
            Content = text,
            Padding = new Thickness(0),
            FontSize = context.Theme.Typography.BodySize,
            VerticalAlignment = VerticalAlignment.Center,
        };
        hyperlink.Click += async (_, _) =>
        {
            try
            {
                await context.Options.LinkHandler.OpenAsync(url);
            }
            catch (Exception ex)
            {
                context.Warn("PURA-LINK-001", $"打开链接失败：{ex.Message}", nameof(LinkInline));
            }
        };

        return hyperlink;
    }

    private static async Task LoadImageAsync(RenderContext context, string url, Image image, TextBlock placeholder)
    {
        try
        {
            var result = await context.Options.ImageLoader.LoadAsync(url).ConfigureAwait(false);
            context.Dispatcher.Post(() =>
            {
                switch (result)
                {
                    case IImageResult.Success success:
                        image.Source = success.Bitmap;
                        image.IsVisible = true;
                        placeholder.IsVisible = false;
                        break;
                    case IImageResult.Refused refused:
                        placeholder.Text = $"[图片未加载: {refused.Reason}]";
                        break;
                    case IImageResult.Failed failed:
                        placeholder.Text = $"[图片加载失败: {failed.Reason}]";
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            context.Warn("PURA-IMG-001", $"图片加载异常：{ex.Message}", nameof(LinkInline));
            context.Dispatcher.Post(() => placeholder.Text = "[图片加载失败]");
        }
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendText(child, builder);
        }

        return builder.ToString();
    }

    private static void AppendText(MdInline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                {
                    AppendText(child, builder);
                }
                break;
        }
    }
}


