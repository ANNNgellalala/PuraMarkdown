using Avalonia.Controls;

namespace PuraMarkdown;

/// <summary>标题大纲条目。</summary>
/// <param name="Level">标题级别（1–6）。</param>
/// <param name="Text">标题纯文本。</param>
/// <param name="Anchor">基于标题文本生成的锚点。</param>
public sealed record PuraHeadingInfo(int Level, string Text, string Anchor);

/// <summary>链接点击事件参数；<see cref="Handled"/> 为 true 时库不再调用 <see cref="Platform.ILinkHandler"/>。</summary>
public sealed class PuraLinkClickedEventArgs(string url, string? text) : EventArgs
{
    /// <summary>链接 URL。</summary>
    public string Url { get; } = url;

    /// <summary>链接显示文本。</summary>
    public string? Text { get; } = text;

    /// <summary>是否已由宿主处理（阻止默认打开行为）。</summary>
    public bool Handled { get; set; }
}

/// <summary>图片开始加载事件参数。</summary>
public sealed class PuraImageEventArgs(string url) : EventArgs
{
    /// <summary>图片 URL。</summary>
    public string Url { get; } = url;
}

/// <summary>图片加载完成事件参数。</summary>
public sealed class PuraImageLoadedEventArgs(string url, Platform.IImageResult result) : EventArgs
{
    /// <summary>图片 URL。</summary>
    public string Url { get; } = url;

    /// <summary>加载结果。</summary>
    public Platform.IImageResult Result { get; } = result;
}
