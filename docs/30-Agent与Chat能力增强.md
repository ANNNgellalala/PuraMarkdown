# Agent 与 Chat 能力增强（无新增 UI 组件）

> 目标：PuraMarkdown 专注 **Markdown 渲染 + Agent 协议/会话数据能力**；不新增消息气泡、按钮等面向 Agent 的 UI 组件，UI 组件由 Arc.Avalonia 等宿主/组件库负责。
> 状态：已实现，当前 105 个测试通过。

---

## 1. Chat：从"列表控件"补到"会话模型 + 行为"

### 1.1 消息模型

`PuraChatMessage` 新增：

| 成员 | 用途 |
| --- | --- |
| `CreatedAt` / `UpdatedAt` | 消息时间线、历史排序 |
| `ContentVersion` | 每次 Markdown 变化递增，便于宿主做增量同步 |
| `Error` | 失败/中断原因 |
| `Metadata` | 宿主任意元数据（工具调用 ID、引用、token 统计等） |
| `Status` | `Complete` / `Streaming` / `Failed` / **`Aborted`** |

### 1.2 历史操作

```csharp
chat.AddMessage(message);
chat.InsertMessage(0, message);
chat.UpdateMessage(message);       // 同 ID 更新或追加
chat.RemoveMessage(id);
chat.ClearMessages();
chat.ReplaceMessages(messages);
chat.FindMessage(id);
```

对应事件：`MessageAdded` / `MessageUpdated` / `MessageRemoved`。

消息属性变化（`Markdown`）会同步到已实现的 `PuraMarkdownView`，不需要重新创建控件。

### 1.3 流式行为

```csharp
var target = new PuraChatMessage { Role = PuraChatRole.Assistant };
var session = chat.BeginStreamingMessage(target);
await session.AppendAsync(token.AsMemory());
await session.CompleteAsync();

chat.IsStreaming;      // 是否流式中
chat.ActiveSession;    // 当前会话
chat.AbortStreaming(); // 保留部分内容，状态 = Aborted
```

- 完成：写回**同一个 message 对象**，`Markdown` = 全文，`Status=Complete`；
- 异常：`Status=Failed`，`Error=LastWorkerError`；
- 中断：`Status=Aborted`，保留已生成内容。

### 1.4 自动滚动策略

```csharp
chat.AutoScrollMode = PuraChatAutoScrollMode.NearBottom; // 默认
// Never / Always
```

`IsNearBottom` 可用于宿主自行控制"跟随/暂停跟随"。

### 1.5 会话快照/持久化

```csharp
var snapshot = chat.CreateSnapshot();
var json = PuraChatSnapshotSerializer.Serialize(snapshot);

var restored = PuraChatSnapshotSerializer.Deserialize(json);
chat.RestoreSnapshot(restored);
```

快照包含消息 ID/角色/Markdown/状态/时间/错误/元数据，不包含 UI 状态。

---

## 2. Agent：协议/状态能力增强

### 2.1 交互状态持久化

核心视图新增：

```csharp
view.InteractionStates;                 // IReadOnlyDictionary<string, PuraInteractionEventArgs>
view.SetInteractionState(state);
view.ApplyInteractionStates(states);    // 恢复后自动重渲染
view.ClearInteractionStates();

string json = PuraInteractionStates.Serialize(view.InteractionStates.Values);
var states = PuraInteractionStates.Deserialize(json);
```

- 状态按 `ComponentId` 保存在视图内；
- Agent choice/confirm 控件实现 `IPuraInteractionStateReceiver`，重渲染/虚拟化回收后自动恢复；
- JSON 使用 **source-generated** context，AOT/裁剪友好。

### 2.2 组件能力发现

```csharp
var plugin = new AgentPlugin();
plugin.ComponentTypes; // ["choice", "confirm", ...]
```

宿主/工具可据此向 Agent 提示可用组件，而不需要硬编码。

### 2.3 组合式注册与重复校验

```csharp
builder
    .UseAgentComponents()
    .UseAgentComponent(new RatingParser(), new RatingRenderer());
```

- 多个插件/模块可向同一 `pura-agent` 围栏追加组件；
- 重复组件类型在构建期 fail-fast（`PuraConfigurationException`）；
- 不会再出现"插件重复注册 pura.agent"。

### 2.4 渲染/校验仍由 Agent 包负责

- JSON → Schema → 组件模型 → 控件；
- 错误路径显示为错误卡（现有 UI，不新增组件类型）；
- 未安装 Agent 包时 `pura-agent` 回退普通代码块。

---

## 3. 明确不在本库范围

以下交由 Arc.Avalonia / 宿主：

- 消息气泡、头像、时间线、输入框、工具调用卡片等 Agent UI 组件；
- 会话管理 UI、设置页、侧边栏；
- 具体 Agent 协议（OpenAI/Anthropic/自研）适配；
- 持久化存储实现（本库只提供快照 DTO/JSON）。

PuraMarkdown 只保证：Markdown 渲染、Agent 围栏协议、交互状态、会话数据模型、虚拟化容器。

---

## 4. 验证

```
dotnet build PuraMarkdown.slnx        → 0 警告 / 0 错误
dotnet test tests/PuraMarkdown.Tests  → 105 通过 / 0 失败
```

新增测试覆盖：

- Chat 操作/事件、快照往返、流式更新同一消息、中断保留部分内容；
- Agent 状态恢复（渲染前/渲染后）、状态 JSON 往返、组件目录、重复组件 fail-fast；
- 部分流式重复完成/中断的 idempotent 行为。

---

## 5. 与 Arc.Avalonia 的边界建议

- Arc.Avalonia 负责视觉/布局/交互壳；
- PuraMarkdown 提供渲染视图与 `PuraChatMessage`/快照/`InteractionStates`；
- 两者通过 `PuraChatMessage`、`InteractionOccurred`、`PuraInteractionStates` 对接，不互相继承控件；
- 需要"消息操作菜单"等 UI 时，由 Arc.Avalonia 基于 `PuraChatMessage.Metadata`/`Id` 实现。



