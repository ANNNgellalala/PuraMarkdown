# 公共 API 设计评审（2026-11-07）

> 目标：在 1.0 前收敛公共 API：**稳定面尽量小、扩展面清晰、内部实现不外溢**，并为插件作者/宿主分别定义契约。
> 结论：建议先做一轮 **API 收敛 + 一致性修复**（允许 0.1 alpha 阶段破坏性调整），再冻结 1.0 公共面。

---

## 1. 设计原则

1. **宿主 API 与插件 API 分层**：宿主只关心控件、事件、流式、主题、平台服务；插件才接触 AST/渲染器/高亮/Schema。
2. **一个配置只有一个来源**：limits、sanitizer、renderer、theme 不要出现两份。
3. **不把 Markdig 低层实现当作稳定 API**：只暴露插件真正需要的 MarkdownObject/Inline。
4. **可测试性**：`PuraMarkdown.Testing` 依赖 `InternalsVisibleTo`，不靠公开内部类型。
5. **1.0 前允许破坏**：当前 `0.1.0-alpha.1`，没有外部兼容负担；1.0 后按 SemVer 管理。

---

## 2. 建议的 API 分层

### 2.1 稳定：宿主 API

| 类型 | 说明 |
| --- | --- |
| `PuraMarkdownView` | 渲染控件；Markdown/Options/事件/流式/大纲/复制 |
| `PuraMarkdownOptions` | 单视图配置：Pipeline、Theme、Limits、平台服务、Localizer、虚拟化、选择 |
| `PuraPipelineBuilder` / `PuraPipeline` | 启动期构建解析与插件环境 |
| `PuraStreamingSession` / `PuraStreamingOptions` | 流式生命周期 |
| `PuraInteractionEventArgs`、`PuraLinkClickedEventArgs`、`PuraImage*` | 交互/链接/图片事件 |
| `PuraTheme` 及 token | 主题定制 |
| `IPuraLocalizer`、`PuraStringKeys` | 本地化 |
| `PuraLimits` | 安全预算 |

### 2.2 稳定：插件 API

| 类型 | 说明 |
| --- | --- |
| `IPuraPlugin` | 插件入口 |
| `IPuraRenderer` / `IIncrementalPuraRenderer` | 块渲染 |
| `IPuraInlineRenderer` | 行内渲染（**当前未接线，必须修复**） |
| `IDiagramRenderer`（未来 `IFenceRenderer`） | 围栏扩展 |
| `ICodeHighlighter` / `HighlightSpan` | 代码高亮 |
| `IAgentComponentParser` / `IAgentComponentRenderer` | Agent 组件 |
| `IAstSanitizer` / `SanitizeResult` | AST 清洗 |
| `PuraRendererRegistry` / `RenderContext` | 渲染上下文 |
| `PuraAgentBlock` | 自定义组件渲染器需要判断 `IsClosed`/读取文本 |

### 2.3 内部或仅测试可见

| 类型 | 建议 |
| --- | --- |
| `PuraAgentBlockParser` / `PuraAgentExtension` | `internal`（Markdig 扩展注册由 builder 负责） |
| `ChoiceParser` / `ConfirmParser` | `internal`（只通过 `UseAgentComponents` 暴露） |
| `StreamingBlockScanner` | `internal`，通过 `InternalsVisibleTo` 给测试 |
| `RendererRegistration` | `internal`；`PuraRendererRegistry.Registrations` 改为 `IReadOnlyList<IPuraRenderer>` |
| `PuraRenderEnvironment` | 可保留，但构造函数 internal；或降为 internal + `PuraPipeline.Environment` 内部 |
| `PuraChoiceControl` / `PuraConfirmControl` | 已 internal，构造函数不必公开 |

---

## 3. 建议的 1.0 前破坏性整理

### 3.1 删除/修复"死配置"

| 项 | 建议 |
| --- | --- |
| `PuraPipelineBuilder.UseLimits` + `PuraRenderEnvironment.Limits` | **删除**，只保留 `PuraMarkdownOptions.Limits`（单一路径） |
| `PuraMarkdownOptions.Logger` | 未使用；**删除**或真正接入日志（建议删除到 1.1 再设计） |
| `PuraStreamingOptions.EnableBlockReuse` | 未使用；**删除** |
| `IPuraPlatformServices` / `DefaultPlatformServices` | 未被 options 使用；**删除**（保留 ImageLoader/LinkHandler/Clipboard 三个契约） |
| `PuraRendererRegistry.Registrations` 的 `RendererRegistration` | 收敛为 `IReadOnlyList<IPuraRenderer>` |

### 3.2 接线缺失的扩展点

```csharp
public PuraPipelineBuilder UseSanitizer(IAstSanitizer sanitizer);
public PuraPipelineBuilder UseInlineRenderer(IPuraInlineRenderer renderer, int priority = 100);
```

- `UseInlineRenderer` 与 `UseRenderer` 分开，文档明确"块 vs 行内"；
- `DefaultInlineRenderer` 在未知 inline 分支调用 `ResolveInline(...).RenderInline(...)`；
- `SanitizeResult` 公开：
  ```csharp
  public void Block(MarkdownObject node);
  public void AddWarning(PuraWarning warning);
  ```
  否则第三方 `IAstSanitizer` 无法阻断节点。

### 3.3 视图生命周期

```csharp
public PuraStreamingSession? ActiveSession { get; }
public bool IsStreaming { get; }
public void AbortStreaming();
public void Dispose(); // 取消 worker、断开事件
```

- `BeginStreaming` 之后宿主不需要额外保存 session；
- `Unloaded` 时自动取消，避免后台 worker 泄漏。

### 3.4 流式选项命名

- `Synchronous` 是测试/控制台专用，建议改为 `RunSynchronously` 并标注 `[EditorBrowsable(EditorBrowsableState.Never)]`，避免宿主误用；
- `AutoScrollToEnd` 仅块级虚拟化内部有效，文档写清楚。

---

## 4. 建议新增（1.1 或 1.0 视讨论）

### 4.1 通用围栏渲染器

`IDiagramRenderer` 只覆盖图表；数学/自定义围栏需要另一套。建议：

```csharp
public interface IFenceRenderer
{
    bool CanRenderFence(string language, string source, RenderContext context);
    Control RenderFence(string language, string source, RenderContext context);
}

public PuraPipelineBuilder UseFenceRenderer(IFenceRenderer renderer, int priority = 100);
```

- `IDiagramRenderer` 作为兼容适配保留一个版本，后续标记 `[Obsolete]`；
- 与现有 `DiagramDispatchRenderer` 回退链一致。

### 4.2 Agent 交互状态（宿主可预置/清空）

```csharp
public sealed record PuraInteractionState(
    string ComponentType,
    string? ComponentId,
    IReadOnlyList<string> SelectedIds,
    bool? Confirmed);

public IReadOnlyDictionary<string, PuraInteractionState> InteractionStates { get; }
public void SetInteractionState(string componentId, PuraInteractionState state);
public void ClearInteractionStates();
```

- 当前 `IPuraInteractionStateReceiver` 只供控件内部恢复；宿主无法预置历史状态；
- 事件仍用 `PuraInteractionEventArgs`（保持 Handled/扩展余地）。

### 4.3 Agent 插件辅助 API

- 公开 `PuraAgentJson`（TryReadString/Bool/Int、TryGetType）；
- 提供 `PuraAgentSchema`（JSON Schema 构造/校验）或默认 Parser 基类；
- 插件只依赖 `PuraMarkdown` 一个包即可写自定义组件。

### 4.4 聊天控件包

`PuraMarkdown.Chat`（独立包）：

```csharp
public sealed class PuraChatMessage { Id, Role, Markdown, Status }
public sealed class PuraChatView : ContentControl
{
    ObservableCollection<PuraChatMessage> Messages { get; }
    PuraStreamingSession BeginStreamingMessage(string? id = null);
    void ScrollToMessage(string id);
}
```

- 消息级虚拟化、流式槽、自动滚动、状态恢复；
- 核心包保持单控件职责。

---

## 5. 需要讨论的开放问题

| # | 问题 | 建议 |
| --- | --- | --- |
| 1 | 1.0 是否要同时冻结"插件 API"？ | 建议冻结核心插件契约（renderer/component/highlighter/sanitizer），`IFenceRenderer`/Agent 辅助 API 可 1.1 |
| 2 | 是否接受删除 `UseLimits`/`Logger`/`EnableBlockReuse`/`IPuraPlatformServices`？ | 建议接受（alpha 阶段，单一路径优先） |
| 3 | `PuraMarkdownView.Blocks`/`BlockRecord` 是否保留公开？ | 建议保留但降级为"高级诊断 API"，`View` 允许 null |
| 4 | `StreamingBlockScanner`、`PuraAgentBlockParser` 等是否 internal？ | 建议 internal + `InternalsVisibleTo` |
| 5 | `IFenceRenderer` 放 1.0 还是 1.1？ | 建议 1.1；1.0 保留 `IDiagramRenderer` |
| 6 | 聊天控件是核心还是独立包？ | 建议独立 `PuraMarkdown.Chat` |
| 7 | `Synchronous` 是否公开？ | 保留但改名/标注，仅供测试控制台 |

---

## 6. 建议执行顺序

1. **API 收敛 PR**：删除死配置、internal 化低层类型、`UseSanitizer`、接线 `IPuraInlineRenderer`；
2. **视图生命周期**：`ActiveSession/IsStreaming/AbortStreaming/Dispose`；
3. **Agent 状态 API**（若确认要宿主预置）；
4. **1.0 冻结**：`PublicAPI.Shipped.txt` + 分析器，SemVer 规则；
5. **1.1**：`IFenceRenderer`、`PuraMarkdown.Chat`、插件元数据。

> 当前 `0.1.0-alpha.1` 阶段建议一次完成 1–2，避免 1.0 后留下历史包袱。
