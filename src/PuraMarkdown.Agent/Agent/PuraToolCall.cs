using System.Globalization;
using System.Text.Json;

namespace PuraMarkdown.Agent;

/// <summary>工具调用状态。</summary>
public enum PuraToolCallStatus
{
    /// <summary>运行中（缺少 status 字段时的默认值）。</summary>
    Running,
    /// <summary>成功。</summary>
    Succeeded,
    /// <summary>失败。</summary>
    Failed,
    /// <summary>已取消。</summary>
    Cancelled,
}

/// <summary>
/// 工具调用模型：由 <c>pura-tool</c> / <c>pura-tool-call</c> 围栏或 <c>pura-agent</c> 的
/// <c>tool-call</c> 组件解析而来，也是 <see cref="Chat.PuraChatMessage.ToolCalls"/> 的元素类型。
/// <para>
/// 载荷示例：<c>{ "id": "call_1", "name": "web_search", "status": "success",
/// "arguments": { "q": "avalonia" }, "result": { "hits": 12 }, "durationMs": 1200 }</c>。
/// </para>
/// </summary>
public sealed record PuraToolCall
{
    /// <summary>工具调用 ID（载荷未提供时为空字符串）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>工具名（必填）。</summary>
    public required string Name { get; init; }

    /// <summary>调用状态；缺省为 <see cref="PuraToolCallStatus.Running"/>。</summary>
    public PuraToolCallStatus Status { get; init; } = PuraToolCallStatus.Running;

    /// <summary>参数显示文本：字符串原样保留，对象/数组/数字/布尔值为原始 JSON 文本；缺失时为 <c>null</c>。</summary>
    public string? Arguments { get; init; }

    /// <summary>结果显示文本（归一化规则同 <see cref="Arguments"/>）。</summary>
    public string? Result { get; init; }

    /// <summary>错误文本（归一化规则同 <see cref="Arguments"/>）。</summary>
    public string? Error { get; init; }

    /// <summary>耗时（来自 <c>durationMs</c>）。</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// 解析工具调用 JSON 载荷；语法错误、非对象载荷、缺少 <c>name</c> 或字段类型非法时返回 <c>false</c>，
    /// 并在 <paramref name="error"/> 给出可读原因（不抛出异常）。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="call">解析成功后的工具调用模型。</param>
    /// <param name="error">解析失败原因。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string json, out PuraToolCall? call, out string? error)
    {
        call = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "工具调用载荷为空";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"JSON 语法错误：{ex.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "工具调用载荷必须是 JSON 对象";
                return false;
            }

            if (!AgentJson.TryReadString(root, "name", required: true, out var name, out var nameError)
                || string.IsNullOrWhiteSpace(name))
            {
                error = nameError?.Message ?? "工具名 name 不能为空";
                return false;
            }

            AgentJson.TryReadString(root, "id", required: false, out var id, out _);

            var status = PuraToolCallStatus.Running;
            if (root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind != JsonValueKind.Null)
            {
                if (statusElement.ValueKind != JsonValueKind.String)
                {
                    error = "status 必须是字符串";
                    return false;
                }

                var rawStatus = statusElement.GetString();
                var parsed = rawStatus?.ToLowerInvariant() switch
                {
                    "running" => PuraToolCallStatus.Running,
                    "success" or "succeeded" => PuraToolCallStatus.Succeeded,
                    "error" or "failed" => PuraToolCallStatus.Failed,
                    "cancelled" or "canceled" => PuraToolCallStatus.Cancelled,
                    _ => (PuraToolCallStatus?)null,
                };
                if (parsed is null)
                {
                    error = $"status 取值无效：{rawStatus}";
                    return false;
                }

                status = parsed.Value;
            }

            if (!TryReadDuration(root, out var duration, out error))
            {
                return false;
            }

            call = new PuraToolCall
            {
                Id = id ?? string.Empty,
                Name = name,
                Status = status,
                Arguments = NormalizeValue(root, "arguments"),
                Result = NormalizeValue(root, "result"),
                Error = NormalizeValue(root, "error"),
                Duration = duration,
            };
            return true;
        }
    }

    internal static string StatusKey(PuraToolCallStatus status)
        => status switch
        {
            PuraToolCallStatus.Succeeded => Localization.PuraStringKeys.ToolCallSucceeded,
            PuraToolCallStatus.Failed => Localization.PuraStringKeys.ToolCallFailed,
            PuraToolCallStatus.Cancelled => Localization.PuraStringKeys.ToolCallCancelled,
            _ => Localization.PuraStringKeys.ToolCallRunning,
        };

    private static string? NormalizeValue(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText(),
        };
    }

    private static bool TryReadDuration(JsonElement root, out TimeSpan? duration, out string? error)
    {
        duration = null;
        error = null;
        if (!root.TryGetProperty("durationMs", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var milliseconds))
        {
            error = "durationMs 必须是数字";
            return false;
        }

        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
        {
            error = "durationMs 必须是非负数";
            return false;
        }

        duration = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}

/// <summary>`pura-agent` 围栏中的 <c>tool-call</c> 组件模型（内部实现，渲染器按类型分支处理）。</summary>
internal sealed record PuraToolCallComponent : PuraAgentComponent
{
    /// <summary>工具调用载荷。</summary>
    public required PuraToolCall Call { get; init; }
}

/// <summary>工具调用耗时的本地化文本（毫秒 / 秒两档）。</summary>
internal static class PuraToolCallDurationText
{
    public static string Format(TimeSpan duration, Localization.IPuraLocalizer localizer)
        => duration.TotalMilliseconds >= 1000
            ? localizer.Get(
                Localization.PuraStringKeys.ToolCallDurationSeconds,
                duration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture))
            : localizer.Get(
                Localization.PuraStringKeys.ToolCallDurationMs,
                duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
}
