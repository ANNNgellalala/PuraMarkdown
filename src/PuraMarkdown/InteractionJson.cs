using System.Text.Json;
using System.Text.Json.Serialization;

namespace PuraMarkdown;

/// <summary>可序列化的交互状态（用于会话持久化/回放）。</summary>
/// <param name="ComponentType">组件类型。</param>
/// <param name="ComponentId">组件实例 ID。</param>
/// <param name="SelectedIds">选中的选项。</param>
/// <param name="Confirmed">确认结果。</param>
/// <param name="Timestamp">发生时间。</param>
public sealed record PuraInteractionState(
    string ComponentType,
    string? ComponentId,
    string[] SelectedIds,
    bool? Confirmed,
    DateTimeOffset Timestamp);

/// <summary>交互状态 JSON 序列化（AOT 友好的 source-generated context）。</summary>
public static class PuraInteractionStates
{
    /// <summary>序列化为 JSON 数组。</summary>
    public static string Serialize(IEnumerable<PuraInteractionEventArgs> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var snapshot = states.Select(state => new PuraInteractionState(
            state.ComponentType,
            state.ComponentId,
            state.SelectedIds.ToArray(),
            state.Confirmed,
            state.Timestamp)).ToArray();
        return JsonSerializer.Serialize(snapshot, PuraInteractionJsonContext.Default.PuraInteractionStateArray);
    }

    /// <summary>从 JSON 数组恢复。</summary>
    public static IReadOnlyList<PuraInteractionEventArgs> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var snapshot = JsonSerializer.Deserialize(json, PuraInteractionJsonContext.Default.PuraInteractionStateArray) ?? [];
        return snapshot.Select(state => new PuraInteractionEventArgs
        {
            ComponentType = state.ComponentType,
            ComponentId = state.ComponentId,
            SelectedIds = state.SelectedIds,
            Confirmed = state.Confirmed,
            Timestamp = state.Timestamp,
        }).ToArray();
    }
}

[JsonSerializable(typeof(PuraInteractionState[]))]
internal partial class PuraInteractionJsonContext : JsonSerializerContext;
