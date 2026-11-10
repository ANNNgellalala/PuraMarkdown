using Avalonia.Controls;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Core;

namespace PuraMarkdown.Agent;

/// <summary>
/// 已解析文档的渲染宿主：接受 <see cref="PuraDocument"/>，不重新解析 Markdown。
/// Arc.Avalonia 可先通过 <see cref="PuraMarkdown.Core.PuraSyntaxPipeline"/> 解析，再交给本控件或自己的 renderer。
/// </summary>
public sealed class PuraDocumentView : ContentControl
{
    private readonly PuraMarkdownView _inner = new();
    private PuraMarkdownOptions? _options;
    private PuraDocument? _document;

    /// <summary>创建文档渲染宿主。</summary>
    public PuraDocumentView()
    {
        Content = _inner;
        _inner.InteractionOccurred += (_, args) => InteractionOccurred?.Invoke(this, args);
        _inner.Warning += (_, args) => Warning?.Invoke(this, args);
        _inner.Announced += (_, text) => Announced?.Invoke(this, text);
        _inner.LinkClicked += (_, args) => LinkClicked?.Invoke(this, args);
        _inner.ImageLoading += (_, args) => ImageLoading?.Invoke(this, args);
        _inner.ImageLoaded += (_, args) => ImageLoaded?.Invoke(this, args);
    }

    /// <summary>渲染配置。</summary>
    public PuraMarkdownOptions? Options
    {
        get => _options;
        set
        {
            _options = value;
            _inner.Options = value;
            if (_document is not null)
            {
                _inner.RenderDocument(_document);
            }
        }
    }

    /// <summary>当前语法文档。</summary>
    public PuraDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            if (value is not null)
            {
                _inner.RenderDocument(value);
            }
        }
    }

    /// <summary>交互事件。</summary>
    public event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;

    /// <summary>警告事件。</summary>
    public event EventHandler<PuraWarningEventArgs>? Warning;

    /// <summary>无障碍播报。</summary>
    public event EventHandler<string>? Announced;

    /// <summary>链接点击。</summary>
    public event EventHandler<PuraLinkClickedEventArgs>? LinkClicked;

    /// <summary>图片加载中。</summary>
    public event EventHandler<PuraImageEventArgs>? ImageLoading;

    /// <summary>图片加载完成。</summary>
    public event EventHandler<PuraImageLoadedEventArgs>? ImageLoaded;

    /// <summary>重新渲染当前文档。</summary>
    public void Render()
    {
        if (_document is not null)
        {
            _inner.RenderDocument(_document);
        }
    }
}

