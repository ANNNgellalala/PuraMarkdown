namespace PuraMarkdown.Agent;

/// <summary>交互事件参数：库只上报，宿主决定是否回传 Agent/服务端。</summary>
public sealed class PuraInteractionEventArgs : EventArgs
{
    /// <summary>组件类型（例如 choice/confirm，或插件自定义类型）。</summary>
    public required string ComponentType { get; init; }

    /// <summary>组件实例 ID。</summary>
    public string? ComponentId { get; init; }

    /// <summary>选中的选项 ID 列表。</summary>
    public IReadOnlyList<string> SelectedIds { get; init; } = [];

    /// <summary>确认组件的结果（确认/取消）。</summary>
    public bool? Confirmed { get; init; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

/// <summary>可接收已上报交互状态的组件控件（虚拟化回收/重渲染后恢复选择状态）。</summary>
public interface IPuraInteractionStateReceiver
{
    /// <summary>恢复上一次交互状态（不回传事件）。</summary>
    /// <param name="state">交互事件参数。</param>
    void ApplyInteractionState(PuraInteractionEventArgs state);
}
