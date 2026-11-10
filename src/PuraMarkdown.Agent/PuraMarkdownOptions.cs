using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Agent.Platform;
using PuraMarkdown.Core.Security;
using PuraMarkdown.Core;

namespace PuraMarkdown.Agent;

/// <summary>控件级配置。Pipeline/Theme 由宿主在启动时构建一次。</summary>
public sealed class PuraMarkdownOptions
{
    /// <summary>Pipeline。</summary>
    public required PuraPipeline Pipeline { get; init; }

    /// <summary>Theme。</summary>
    public PuraTheme Theme { get; init; } = PuraTheme.Light;

    /// <summary>跟随 Avalonia 系统主题（深色时使用 <see cref="DarkTheme"/> 或内置 Dark）。</summary>
    public bool FollowSystemTheme { get; init; }

    /// <summary>DarkTheme。</summary>
    public PuraTheme? DarkTheme { get; init; }

    /// <summary>false 时交互组件全部禁用、不产生事件（只读模式）。</summary>
    public bool InteractionEnabled { get; init; } = true;

    /// <summary>UrlPolicy。</summary>
    public IUrlPolicy UrlPolicy { get; init; } = DefaultUrlPolicy.Instance;

    /// <summary>ImageLoader。</summary>
    public IImageLoader ImageLoader { get; init; } = NullImageLoader.Instance;

    /// <summary>LinkHandler。</summary>
    public ILinkHandler LinkHandler { get; init; } = NullLinkHandler.Instance;

    /// <summary>Clipboard。</summary>
    public IPuraClipboard Clipboard { get; init; } = NullClipboard.Instance;


    /// <summary>Limits。</summary>
    public PuraLimits Limits { get; init; } = new();

    /// <summary>本地化文案（默认中文；内置 <see cref="ChinesePuraLocalizer"/>）。</summary>
    public IPuraLocalizer Localizer { get; init; } = ChinesePuraLocalizer.Instance;

    /// <summary>正文是否可选中/复制（使用 SelectableTextBlock）。默认 false。</summary>
    public bool EnableTextSelection { get; init; }

    /// <summary>
    /// 启用块级虚拟化：控件内部使用 ItemsRepeater 只实例化视口内的块，适合单条超长消息。
    /// 启用后控件自带 ScrollViewer，宿主不要再外层再包一层滚动容器；默认 false（StackPanel）。
    /// </summary>
    public bool EnableBlockVirtualization { get; init; }

    internal PuraSyntaxOptions CreateSyntaxOptions()
        => Pipeline.Syntax.CreateOptions(Limits, UrlPolicy);
}





