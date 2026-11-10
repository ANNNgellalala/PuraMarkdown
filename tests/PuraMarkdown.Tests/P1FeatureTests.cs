using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Markdig.Syntax;
using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Core.Security;
using PuraMarkdown.Core;
using PuraMarkdown.Agent.Platform;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Agent.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class P1FeatureTests
{
    private static PuraMarkdownOptions CreateOptions(
        IPuraLocalizer? localizer = null,
        bool selection = false,
        IPuraClipboard? clipboard = null,
        ILinkHandler? linkHandler = null)
        => new()
        {
            Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
            Theme = PuraTheme.Light,
            Localizer = localizer ?? ChinesePuraLocalizer.Instance,
            EnableTextSelection = selection,
            Clipboard = clipboard ?? NullClipboard.Instance,
            LinkHandler = linkHandler ?? NullLinkHandler.Instance,
        };

    [AvaloniaFact]
    public void Default_localization_is_chinese_and_can_be_replaced()
    {
        var chinese = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "```csharp\nvar x = 1;\n```",
        };
        Assert.Contains(ControlTreeDumper.Find<Button>(chinese), button => Equals(button.Content, "复制"));

        var english = new PuraMarkdownView
        {
            Options = CreateOptions(new EnglishLocalizer()),
            Markdown = "```csharp\nvar x = 1;\n```",
        };
        Assert.Contains(ControlTreeDumper.Find<Button>(english), button => Equals(button.Content, "Copy"));
    }

    [AvaloniaFact]
    public async Task Text_selection_plain_text_and_copy_all()
    {
        var clipboard = new RecordingClipboard();
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(selection: true, clipboard: clipboard),
            Markdown = "# Title\n\nHello **world**.",
        };

        Assert.NotEmpty(ControlTreeDumper.Find<SelectableTextBlock>(view));
        Assert.Contains("Title", view.GetPlainText(), StringComparison.Ordinal);
        Assert.Contains("Hello world", view.GetPlainText(), StringComparison.Ordinal);

        await view.CopyAllAsync();
        Assert.Contains("Hello world", clipboard.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Outline_and_scroll_to_anchor()
    {
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(),
            Markdown = "# First\n\n## Second\n\ntext",
        };

        var outline = view.GetOutline();
        Assert.Equal(2, outline.Count);
        Assert.Equal(1, outline[0].Level);
        Assert.Equal("First", outline[0].Text);
        Assert.Equal("first", outline[0].Anchor);
        Assert.True(view.ScrollToAnchor("second"));
        Assert.False(view.ScrollToAnchor("missing"));
    }

    [AvaloniaFact]
    public async Task Streaming_lifecycle_events_and_append_async()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var completed = false;
        var aborted = false;
        var session = view.BeginStreaming();
        session.Completed += (_, _) => completed = true;
        session.Aborted += (_, _) => aborted = true;

        await session.AppendAsync("hello".AsMemory());
        await session.CompleteAsync();

        Assert.True(completed);
        Assert.False(aborted);
        Assert.Contains("hello", ControlTreeDumper.Dump(view));
        Assert.Equal("hello", session.CurrentText);
    }

    [AvaloniaFact]
    public void Image_loading_and_loaded_events_are_raised()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions
            {
                Pipeline = new PuraPipelineBuilder().UseCommonMark().UseGfm().Build(),
                ImageLoader = new FailingImageLoader(),
            },
        };
        PuraImageEventArgs? loading = null;
        PuraImageLoadedEventArgs? loaded = null;
        view.ImageLoading += (_, args) => loading = args;
        view.ImageLoaded += (_, args) => loaded = args;
        view.Markdown = "![alt](https://example.com/image.png)";

        Dispatcher.UIThread.RunJobs();

        Assert.Equal("https://example.com/image.png", loading?.Url);
        Assert.NotNull(loaded);
        Assert.IsType<IImageResult.Failed>(loaded!.Result);
    }

    [AvaloniaFact]
    public void Custom_sanitizer_can_block_nodes()
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions
            {
                Pipeline = new PuraPipelineBuilder()
                    .UseCommonMark()
                    .UseGfm()
                    .UseSanitizer(new BlockAllSanitizer())
                    .Build(),
            },
        };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);
        Assert.IsType<BlockAllSanitizer>(view.Options!.Pipeline.Syntax.Options.Sanitizer);
        view.Markdown = "blocked";

        Assert.Empty(view.Blocks);
        Assert.Contains(warnings, warning => warning.Code == "PURA-SEC-TEST");
    }

    [AvaloniaFact]
    public async Task View_exposes_streaming_state_and_can_abort()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });
        session.Append("x");
        Assert.True(view.IsStreaming);
        Assert.Same(session, view.ActiveSession);

        view.AbortStreaming();
        await Task.Yield();

        Assert.False(view.IsStreaming);
        view.Dispose();
    }

    [AvaloniaFact]
    public void Link_click_event_can_handle_without_default_handler()
    {
        var linkHandler = new RecordingLinkHandler();
        var view = new PuraMarkdownView
        {
            Options = CreateOptions(linkHandler: linkHandler),
            Markdown = "[site](https://example.com)",
        };
        PuraLinkClickedEventArgs? clicked = null;
        view.LinkClicked += (_, args) =>
        {
            clicked = args;
            args.Handled = true;
        };

        var button = ControlTreeDumper.Find<HyperlinkButton>(view).Single();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(clicked);
        Assert.Equal("https://example.com", clicked!.Url);
        Assert.Equal(0, linkHandler.Count);
    }

    private sealed class BlockAllSanitizer : IAstSanitizer
    {
        public SanitizeResult Sanitize(MarkdownDocument document, PuraSyntaxOptions options)
        {
            var result = new SanitizeResult();
            foreach (var block in document)
            {
                result.Block(block);
            }

            result.AddWarning("PURA-SEC-TEST: blocked");
            return result;
        }
    }

    private sealed class EnglishLocalizer : IPuraLocalizer
    {
        public string Get(string key, params object?[] args)
            => key switch
            {
                PuraStringKeys.Copy => "Copy",
                PuraStringKeys.Copied => "Copied",
                PuraStringKeys.CopyFailed => "Copy failed",
                _ => key,
            };
    }

    private sealed class RecordingClipboard : IPuraClipboard
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingImageLoader : IImageLoader
    {
        public Task<IImageResult> LoadAsync(string url, CancellationToken cancellationToken = default)
            => Task.FromResult<IImageResult>(new IImageResult.Failed("no network"));
    }

    private sealed class RecordingLinkHandler : ILinkHandler
    {
        public int Count { get; private set; }

        public Task OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}








