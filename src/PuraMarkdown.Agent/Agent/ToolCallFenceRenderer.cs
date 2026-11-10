using Avalonia.Controls;
using PuraMarkdown.Agent.Localization;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>
/// 工具调用围栏渲染器：接管 <c>pura-tool</c>（别名 <c>pura-tool-call</c>）围栏，按 JSON 载荷渲染工具调用卡片。
/// 载荷无效时降级为普通代码块并上报 <c>PURA-DATA-*</c> 告警，永不抛出异常。
/// </summary>
internal sealed class ToolCallFenceRenderer : IFenceRenderer
{
    /// <summary>主语言标识。</summary>
    internal const string Language = "pura-tool";

    /// <summary>别名语言标识。</summary>
    internal const string AliasLanguage = "pura-tool-call";

    /// <inheritdoc/>
    public string Name => "pura.tool.fence";

    /// <summary>是否为工具调用围栏语言（大小写不敏感）。</summary>
    /// <param name="language">围栏语言。</param>
    internal static bool IsToolCallLanguage(string? language)
        => string.Equals(language, Language, StringComparison.OrdinalIgnoreCase)
           || string.Equals(language, AliasLanguage, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool CanRender(string language, string source, bool isClosed, RenderContext context)
        => IsToolCallLanguage(language);

    /// <inheritdoc/>
    public Control Render(string language, string source, bool isClosed, RenderContext context)
    {
        var raw = source.Trim();
        var theme = context.Theme;
        var localizer = context.Options.Localizer;
        var maxChars = context.Options.Limits.MaxFenceChars;

        if (raw.Length > maxChars)
        {
            context.Warn("PURA-LIM-004", $"工具调用载荷超过 {maxChars} 字符", nameof(ToolCallFenceRenderer));
            return ToolCallCards.CodeFallback(theme, raw[..maxChars]);
        }

        if (!isClosed)
        {
            // 流式未闭合：载荷通常不完整，按「运行中」渲染，且不产生告警（避免逐帧噪声）。
            if (PuraToolCall.TryParse(raw, out var pending, out _) && pending is not null)
            {
                return new PuraToolCallCard(
                    pending with { Status = PuraToolCallStatus.Running },
                    theme,
                    localizer,
                    context.Options.Clipboard);
            }

            return new PuraToolCallCard(
                new PuraToolCall
                {
                    Name = localizer.Get(PuraStringKeys.ToolCallPending),
                    Status = PuraToolCallStatus.Running,
                },
                theme,
                localizer,
                context.Options.Clipboard);
        }

        if (!PuraToolCall.TryParse(raw, out var call, out var error) || call is null)
        {
            context.Warn("PURA-DATA-002", $"pura-tool 载荷校验失败：{error}", nameof(ToolCallFenceRenderer));
            return ToolCallCards.CodeFallback(theme, raw);
        }

        return new PuraToolCallCard(call, theme, localizer, context.Options.Clipboard);
    }
}
