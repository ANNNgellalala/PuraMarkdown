using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace PuraMarkdown.Rendering;

/// <summary>
/// Emoji 行内 Run 标记：单独使用 emoji 字体族，且不参与强调格式（粗体/斜体）。
/// Avalonia 的彩色 emoji 在非 Regular 字重下可能退化为黑白，且 macOS 上加载彩色字体代价很高。
/// </summary>
internal sealed class EmojiRun : Run
{
    private static readonly FontFamily EmojiFont =
        new("Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji");

    /// <summary>创建 emoji Run。</summary>
    public static EmojiRun Create(string text, RenderContext context) => new()
    {
        Text = text,
        FontFamily = EmojiFont,
        FontSize = context.Theme.Typography.BodySize,
        FontWeight = FontWeight.Normal,
        FontStyle = FontStyle.Normal,
    };
}
