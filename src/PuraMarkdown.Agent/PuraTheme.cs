using Avalonia.Media;

namespace PuraMarkdown.Agent;

/// <summary>主题 token：默认渲染器只使用这些值，不硬编码颜色/字号。</summary>
public sealed class PuraTheme
{
    /// <summary>Name。</summary>
    public required string Name { get; init; }
    /// <summary>Colors。</summary>
    public required PuraColors Colors { get; init; }
    /// <summary>Typography。</summary>
    public required PuraTypography Typography { get; init; }
    /// <summary>Spacing。</summary>
    public required PuraSpacing Spacing { get; init; }
    /// <summary>CodeTheme。</summary>
    public PuraCodeTheme CodeTheme { get; init; } = PuraCodeTheme.Default;

    /// <summary>Light。</summary>
    public static PuraTheme Light { get; } = new()
    {
        Name = "light",
        Colors = new PuraColors(
            Text: Color.Parse("#1F2328"),
            Background: Color.Parse("#FFFFFF"),
            Border: Color.Parse("#D0D7DE"),
            Accent: Color.Parse("#0969DA"),
            Muted: Color.Parse("#57606A"),
            CodeBackground: Color.Parse("#F6F8FA"),
            CodeBorder: Color.Parse("#D0D7DE"),
            Danger: Color.Parse("#CF222E")),
        Typography = PuraTypography.Default,
        Spacing = PuraSpacing.Default,
    };

    /// <summary>Dark。</summary>
    public static PuraTheme Dark { get; } = new()
    {
        Name = "dark",
        Colors = new PuraColors(
            Text: Color.Parse("#E6EDF3"),
            Background: Color.Parse("#0D1117"),
            Border: Color.Parse("#30363D"),
            Accent: Color.Parse("#2F81F7"),
            Muted: Color.Parse("#8B949E"),
            CodeBackground: Color.Parse("#161B22"),
            CodeBorder: Color.Parse("#30363D"),
            Danger: Color.Parse("#F85149")),
        Typography = PuraTypography.Default,
        Spacing = PuraSpacing.Default,
    };
}

/// <summary>PuraColors。</summary>
public sealed record PuraColors(
    Color Text,
    Color Background,
    Color Border,
    Color Accent,
    Color Muted,
    Color CodeBackground,
    Color CodeBorder,
    Color Danger);

/// <summary>PuraTypography。</summary>
public sealed record PuraTypography(
    double BodySize,
    double Heading1Size,
    double Heading2Size,
    double Heading3Size,
    double Heading4Size,
    double Heading5Size,
    double Heading6Size,
    FontFamily BodyFont,
    FontFamily MonoFont)
{
    /// <summary>Default。</summary>
    public static PuraTypography Default { get; } = new(
        BodySize: 14,
        Heading1Size: 26,
        Heading2Size: 22,
        Heading3Size: 18,
        Heading4Size: 16,
        Heading5Size: 14,
        Heading6Size: 13,
        BodyFont: FontFamily.Default,
        MonoFont: new FontFamily("Consolas, Cascadia Mono, DejaVu Sans Mono, monospace"));

    /// <summary>HeadingSize。</summary>
    /// <param name="level">level 参数。</param>
    public double HeadingSize(int level) => level switch
    {
        1 => Heading1Size,
        2 => Heading2Size,
        3 => Heading3Size,
        4 => Heading4Size,
        5 => Heading5Size,
        _ => Heading6Size,
    };
}

/// <summary>PuraSpacing。</summary>
public sealed record PuraSpacing(
    double BlockSpacing,
    double ListIndent,
    double CodePadding,
    double CodeBlockSpacing,
    double QuoteIndent)
{
    /// <summary>Default。</summary>
    public static PuraSpacing Default { get; } = new(
        BlockSpacing: 8,
        ListIndent: 18,
        CodePadding: 12,
        CodeBlockSpacing: 10,
        QuoteIndent: 12);
}

/// <summary>PuraCodeTheme。</summary>
/// <param name="PlainText">PlainText 参数。</param>
/// <param name="Comment">Comment 参数。</param>
/// <param name="Keyword">Keyword 参数。</param>
/// <param name="String">String 参数。</param>
/// <param name="Number">Number 参数。</param>
public sealed record PuraCodeTheme(Color PlainText, Color Comment, Color Keyword, Color String, Color Number)
{
    /// <summary>Default。</summary>
    public static PuraCodeTheme Default { get; } = new(
        PlainText: Color.Parse("#24292F"),
        Comment: Color.Parse("#6E7781"),
        Keyword: Color.Parse("#CF222E"),
        String: Color.Parse("#0A3069"),
        Number: Color.Parse("#0550AE"));

    /// <summary>Resolve。</summary>
    /// <param name="token">token 参数。</param>
    public Color Resolve(string token) => token.ToLowerInvariant() switch
    {
        "comment" => Comment,
        "keyword" => Keyword,
        "string" => String,
        "number" => Number,
        _ => PlainText,
    };
}

