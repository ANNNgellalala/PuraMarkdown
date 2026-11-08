# Agent 插件化与公共 API 收敛实现（2026-11-07）

> 对应 [插件通用接口与 Agent 插件化讨论](24-插件通用接口与Agent插件化讨论.md) 的推荐方案，已完成核心契约重构、Agent 剥离、Mermaid 迁移与 Chat 包。

---

## 1. 核心插件契约（PuraMarkdown）

### 1.1 `IFenceRenderer` 取代 `IDiagramRenderer`

```csharp
public interface IFenceRenderer
{
    string Name { get; }
    bool CanRender(string language, string source, bool isClosed, RenderContext context);
    Control Render(string language, string source, bool isClosed, RenderContext context);
}
```

- `isClosed` 由 Markdig `FencedCodeBlock.ClosingFencedCharCount` 提供，插件可区分"流式中未闭合"与"已完成围栏"；
- `PuraPipelineBuilder.UseFenceRenderer(renderer, priority)`；
- 同一语言按 priority 选择，无匹配回退普通代码块；
- 同 `Name` 重复注册构建期 fail-fast。

### 1.2 行内插件接线

- 新增 `PuraPipelineBuilder.UseInlineRenderer(IPuraInlineRenderer, priority)`；
- `DefaultInlineRenderer` 在未知 inline 节点时调用 `ResolveInline().RenderInline()`，不再静默 `ToString()`。

### 1.3 清洗器可扩展

- 新增 `UseSanitizer(IAstSanitizer)`；
- `SanitizeResult.Block(MarkdownObject)` / `AddWarning(string)` 改为公开，自定义 sanitizer 可以真正阻断节点。

### 1.4 视图生命周期

```csharp
public PuraStreamingSession? ActiveSession { get; }
public bool IsStreaming { get; }
public void AbortStreaming();
public void Dispose();
```

### 1.5 删除/收敛的旧 API

| 删除/调整 | 说明 |
| --- | --- |
| `PuraPipelineBuilder.UseLimits` / `PuraRenderEnvironment.Limits` | limits 只保留在 `PuraMarkdownOptions.Limits` |
| `PuraMarkdownOptions.Logger` | 未使用，删除 |
| `PuraStreamingOptions.EnableBlockReuse` | 未使用，删除 |
| `IPuraPlatformServices` / `DefaultPlatformServices` | 未被使用，删除 |
| `IDiagramRenderer` / `UseDiagramRenderer` | 由 `IFenceRenderer` 取代 |
| `PuraPipelineBuilder.UseAgentComponents` 等 | 移至 `PuraMarkdown.Agent` 扩展 |
| `PuraComponentRegistry` / `IAgentComponent*` | 移至 Agent 包 |
| `PuraAgentBlock`/Parser/Extension | 删除；`pura-agent` 改为围栏渲染 |
| `RendererRegistration` | 内部化；注册表暴露 `IReadOnlyList<IPuraRenderer> Renderers` |

---

## 2. PuraMarkdown.Agent

### 2.1 包职责

`pura-agent` 围栏、JSON 组件模型/Schema、choice/confirm、交互状态恢复、Agent 文案。

### 2.2 用法

```csharp
using PuraMarkdown.Agent;

var pipeline = new PuraPipelineBuilder()
    .UseCommonMark()
    .UseGfm()
    .UsePlugin(new AgentPlugin())   // 或 .UseAgentComponents()
    .Build();
```

自定义组件：

```csharp
new AgentPlugin(
    parsers: [new RatingParser()],
    renderers: [new RatingRenderer()]);
```

`AgentFenceRenderer`：

- 语言 `pura-agent`；
- `context.IsTail` 或 `isClosed == false` → 加载占位；
- 闭合后 JSON → Schema → 组件控件；
- 组件无 `id` 时基于载荷哈希生成稳定 ID，保证交互状态可恢复；
- 未安装 Agent 插件时，`pura-agent` 自然回退为代码块。

### 2.3 依赖变化

- 核心 `PuraMarkdown` 不再依赖 `JsonSchema.Net`；
- `PuraMarkdown.Agent` 依赖核心 + JsonSchema.Net。

---

## 3. Mermaid 迁移

`MermaidPlugin` 已从 `IDiagramRenderer` 迁至 `IFenceRenderer`：

```csharp
builder.UseFenceRenderer(new MermaidDiagramRenderer());
```

行为不变：流式占位、ASCII 渲染、解析失败错误卡、无插件回退代码块。

---

## 4. PuraMarkdown.Chat

```csharp
var chat = new PuraChatView { Options = options };
chat.AddMessage(new PuraChatMessage { Role = PuraChatRole.User, Markdown = "你好" });

var session = chat.BeginStreamingMessage(new PuraChatMessage { Role = PuraChatRole.Assistant });
await session.AppendAsync("回答".AsMemory());
await session.CompleteAsync(); // 自动加入历史并滚动到底部
```

- 消息级 `ItemsRepeater` 虚拟化；
- 流式消息独立底部槽；
- `ScrollToEnd` / `ScrollToMessage`；
- 不依赖 Agent 包。

---

## 5. 迁移指南（alpha 破坏性变更）

| 旧用法 | 新用法 |
| --- | --- |
| `.UseAgentComponents()` (核心) | `using PuraMarkdown.Agent;` 后 `.UseAgentComponents()` 或 `.UsePlugin(new AgentPlugin())` |
| `.UseDiagramRenderer(x)` | `.UseFenceRenderer(new XFenceRenderer())` |
| `IDiagramRenderer` | `IFenceRenderer`（新增 `Name`、`isClosed` 参数） |
| `builder.UseLimits(...)` | `new PuraMarkdownOptions { Limits = ... }` |
| `new PuraMarkdownOptions { Logger = ... }` | 删除（后续日志 API 再设计） |
| `PuraAgentBlock` / `PuraAgentBlockParser` | 不再暴露；通过 `IFenceRenderer` 扩展 `pura-agent` |
| `PuraRendererRegistry.Registrations` | `PuraRendererRegistry.Renderers` |

---

## 6. 验证

```
dotnet build PuraMarkdown.slnx        → 0 警告 / 0 错误
dotnet test tests/PuraMarkdown.Tests  → 119 通过 / 0 失败
dotnet pack -c Release               → 7 个 nupkg（核心/Agent/Chat/Testing/ColorCode/Mermaid）
```

新增测试：

- `Agent_fence_renders_choice_component` / `Unclosed_agent_fence_renders_loading_placeholder`（围栏实现）；
- `Chat_view_virtualizes_messages` / `Chat_streaming_completion_adds_history_message`；
- `Custom_sanitizer_can_block_nodes`；
- `View_exposes_streaming_state_and_can_abort`；
- 既有测试全部通过（当前共 119 个）。

---

## 7. 后续

1. `PublicAPI.Shipped.txt` + API 分析器，冻结 1.0 公共面；
2. Agent 自定义组件的组合式注册（多个插件共同向同一 Agent 围栏添加组件）；
3. Chat 包接入 Demo；
4. 文档/ADR 的旧接口引用清理。












