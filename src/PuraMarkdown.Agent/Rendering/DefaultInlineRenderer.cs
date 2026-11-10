using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Agent.Platform;
using PuraMarkdown.Core.Security;
using PuraMarkdown.Agent.Rendering;

using MdInline = Markdig.Syntax.Inlines.Inline;

namespace PuraMarkdown.Agent.Rendering;

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
            case EmojiInline emoji:
                target.Add(EmojiRun.Create(emoji.Content.ToString(), context));
                return;

            case MathInline math:
                if (!TryRenderInlinePlugin(math, target, context))
                {
                    PuraMathRenderer.AppendInline(math.Content.ToString(), target, context);
                }

                return;

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

            case FootnoteLink footnoteLink:
                RenderFootnoteLink(footnoteLink, target, context);
                return;

            default:
                if (TryRenderInlinePlugin(inline, target, context))
                {
                    return;
                }

                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    target.Add(new Run(text));
                }
                return;
        }
    }

    /// <summary>优先询问插件行内渲染器（MathInline/未知行内节点）；失败时回退核心默认。</summary>
    private static bool TryRenderInlinePlugin(MdInline inline, InlineCollection target, RenderContext context)
    {
        var renderer = context.Renderers.ResolveInline(inline, context);
        if (renderer is null)
        {
            return false;
        }

        try
        {
            target.Add(renderer.RenderInline(inline, context));
            return true;
        }
        catch (Exception ex)
        {
            context.Warn(
                "PURA-RENDER-011",
                $"行内渲染器 {renderer.GetType().Name} 渲染失败，回退默认：{ex.Message}",
                inline.GetType().Name,
                renderer.GetType().Name);
            return false;
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, InlineCollection target, RenderContext context)
    {
        var temp = new InlineCollection();
        RenderChildren(emphasis, temp, context);

        var delimiter = emphasis.DelimiterChar;
        var count = emphasis.DelimiterCount;
        var isStrike = delimiter == '~' && count >= 2;
        var isSubscript = delimiter == '~' && count == 1;
        var isSuperscript = delimiter == '^';
        var isInserted = delimiter == '+';
        var isMarked = delimiter == '=';
        var isStrong = !isStrike && !isSubscript && !isSuperscript && !isInserted && !isMarked && count >= 2;
        var isItalic = !isStrike && !isSubscript && !isSuperscript && !isInserted && !isMarked && count % 2 == 1;

        foreach (var child in temp.ToList())
        {
            // Emoji 不参与粗体/斜体等强调格式：Avalonia 的彩色 emoji 在非 Regular 字重下会退化为黑白。
            if (child is EmojiRun)
            {
                target.Add(child);
                continue;
            }

            if (child is Run run)
            {
                if (isStrike)
                {
                    run.TextDecorations = TextDecorations.Strikethrough;
                }
                else if (isSubscript)
                {
                    run.BaselineAlignment = BaselineAlignment.Subscript;
                    run.FontSize = Math.Max(8, run.FontSize - 2);
                }
                else if (isSuperscript)
                {
                    run.BaselineAlignment = BaselineAlignment.Superscript;
                    run.FontSize = Math.Max(8, run.FontSize - 2);
                }
                else if (isInserted)
                {
                    run.TextDecorations = TextDecorations.Underline;
                }
                else if (isMarked)
                {
                    run.Background = new SolidColorBrush(context.Theme.Colors.CodeBackground);
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

    private static void RenderFootnoteLink(FootnoteLink link, InlineCollection target, RenderContext context)
    {
        var order = link.Footnote?.Order ?? 0;
        var isBack = link.IsBackLink;
        var content = new TextBlock
        {
            Inlines = new InlineCollection
            {
                new Run(isBack ? "↩" : order.ToString())
                {
                    FontSize = isBack ? context.Theme.Typography.BodySize : context.Theme.Typography.BodySize - 3,
                    BaselineAlignment = isBack ? BaselineAlignment.Baseline : BaselineAlignment.Superscript,
                },
            },
        };
        var linkButton = new HyperlinkButton
        {
            Content = content,
            Padding = new Thickness(0, 0, isBack ? 6 : 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(linkButton, isBack
            ? context.Options.Localizer.Get(PuraStringKeys.FootnoteBacklink)
            : order.ToString());
        if (isBack)
        {
            linkButton.Click += (_, _) => context.NavigateSink?.Invoke($"footnote-ref-{order}");
        }
        else
        {
            context.AnchorSink?.Invoke($"footnote-ref-{order}", linkButton);
            linkButton.Click += (_, _) => context.NavigateSink?.Invoke($"footnote-{order}");
        }

        target.Add(new InlineUIContainer(linkButton));
    }

    private static void RenderLink(LinkInline link, InlineCollection target, RenderContext context)
    {
        var theme = context.Theme;
        var localizer = context.Options.Localizer;
        var blocked = context.Sanitized.IsBlocked(link);
        var url = link.Url ?? string.Empty;

        if (link.IsImage)
        {
            var alt = GetInlineText(link) is { Length: > 0 } altText
                ? altText
                : localizer.Get(PuraStringKeys.Image);
            var placeholder = new TextBlock
            {
                Text = localizer.Get(PuraStringKeys.ImagePlaceholder, alt),
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
                placeholder.Text = localizer.Get(PuraStringKeys.ImageBlocked, url);
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

        target.Add(new InlineUIContainer(CreateHyperlink(linkText, url, context)));
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
            var args = new PuraLinkClickedEventArgs(url, text);
            context.LinkClickSink?.Invoke(args);
            if (args.Handled)
            {
                return;
            }

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
        var localizer = context.Options.Localizer;
        context.ImageLoadingSink?.Invoke(url);
        try
        {
            var result = await context.Options.ImageLoader.LoadAsync(url).ConfigureAwait(false);
            context.Dispatcher.Post(() =>
            {
                context.ImageLoadedSink?.Invoke(url, result);
                switch (result)
                {
                    case IImageResult.Success success:
                        image.Source = success.Bitmap;
                        image.IsVisible = true;
                        placeholder.IsVisible = false;
                        break;
                    case IImageResult.Refused refused:
                        placeholder.Text = localizer.Get(PuraStringKeys.ImageNotLoaded, refused.Reason);
                        break;
                    case IImageResult.Failed failed:
                        placeholder.Text = localizer.Get(PuraStringKeys.ImageLoadFailed, failed.Reason);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            context.Warn("PURA-IMG-001", $"图片加载异常：{ex.Message}", nameof(LinkInline));
            var failed = new IImageResult.Failed(ex.Message);
            context.Dispatcher.Post(() =>
            {
                context.ImageLoadedSink?.Invoke(url, failed);
                placeholder.Text = localizer.Get(PuraStringKeys.ImageLoadFailed, string.Empty);
            });
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






