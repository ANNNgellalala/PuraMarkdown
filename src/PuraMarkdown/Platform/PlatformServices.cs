using Avalonia.Media.Imaging;

namespace PuraMarkdown.Platform;

/// <summary>IImageResult。</summary>
public abstract record IImageResult
{
    /// <summary>Success。</summary>
    /// <param name="Bitmap">Bitmap 参数。</param>
    public sealed record Success(Bitmap Bitmap) : IImageResult;
    /// <summary>Refused。</summary>
    /// <param name="Reason">Reason 参数。</param>
    public sealed record Refused(string Reason) : IImageResult;
    /// <summary>Failed。</summary>
    /// <param name="Reason">Reason 参数。</param>
    public sealed record Failed(string Reason) : IImageResult;
}

/// <summary>IImageLoader。</summary>
public interface IImageLoader
{
    /// <summary>LoadAsync。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    Task<IImageResult> LoadAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>ILinkHandler。</summary>
public interface ILinkHandler
{
    /// <summary>OpenAsync。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>IPuraClipboard。</summary>
public interface IPuraClipboard
{
    /// <summary>SetTextAsync。</summary>
    /// <param name="text">text 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>默认图片加载器：不联网，返回拒绝。</summary>
public sealed class NullImageLoader : IImageLoader
{
    /// <summary>Instance。</summary>
    public static NullImageLoader Instance { get; } = new();

    /// <summary>LoadAsync。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    public Task<IImageResult> LoadAsync(string url, CancellationToken cancellationToken = default)
        => Task.FromResult<IImageResult>(new IImageResult.Refused("默认不加载远程图片"));
}

/// <summary>默认链接处理：不自动打开，可由宿主替换。</summary>
public sealed class NullLinkHandler : ILinkHandler
{
    /// <summary>Instance。</summary>
    public static NullLinkHandler Instance { get; } = new();

    /// <summary>OpenAsync。</summary>
    /// <param name="url">url 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    public Task OpenAsync(string url, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>默认剪贴板：不做事（测试/无 UI 环境）。</summary>
public sealed class NullClipboard : IPuraClipboard
{
    /// <summary>Instance。</summary>
    public static NullClipboard Instance { get; } = new();

    /// <summary>SetTextAsync。</summary>
    /// <param name="text">text 参数。</param>
    /// <param name="cancellationToken">cancellationToken 参数。</param>
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>平台能力聚合（剪贴板等）；未来多平台时替换实现即可。</summary>
public interface IPuraPlatformServices
{
    /// <summary>Clipboard。</summary>
    IPuraClipboard Clipboard { get; }
}

/// <summary>DefaultPlatformServices。</summary>
public sealed class DefaultPlatformServices : IPuraPlatformServices
{
    /// <summary>Clipboard。</summary>
    public IPuraClipboard Clipboard { get; } = NullClipboard.Instance;
}
