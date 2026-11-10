> 历史文档：记录当时的设计/实现，API 可能已过时。当前接入请阅读 [应用集成指南](26-应用集成指南.md) / [迁移指南](27-迁移指南-0.1.0-alpha.1.md) / [公共 API 参考](28-公共API参考.md)。
>
> 包改名脚注：2026-11-10 起包身份已改名为 `PuraMarkdown.Core` / `PuraMarkdown.Agent` / `PuraMarkdown.Extension`，命名空间随包同步变更；本文为当时记录，当前包清单见 [ADR-0028](adr/0028-package-identity-rename.md)。

# 公共 API 与功能扩展建议（2026-11-07）

> 目标：在不破坏"只读、Markdig AST 直渲染、无 IR"架构的前提下，补齐宿主真正需要的公共 API 与 Markdown 能力。
> 结论：当前核心渲染/流式/虚拟化能力已稳定，公共 API 的下一阶段重点应从"能不能渲染"转向**宿主集成、可扩展性、可访问性、国际化、导出与诊断**。

---

## 1. 当前公共 API 概览

| 层 | 已公开 |
| --- | --- |
| 控件 | `PuraMarkdownView`（Markdown/Options/Events/Blocks/BeginStreaming/Render/Announce/Prewarm/ScrollToEnd） |
| 管道 | `PuraPipelineBuilder`（UseCommonMark/UseGfm/UseAgentComponents/UseRenderer/UseDiagramRenderer/UseCodeHighlighter/UseMarkdownExtension/UsePlugin/UseLimits）、`PuraPipeline`、`PuraRenderEnvironment` |
| 配置 | `PuraMarkdownOptions`、`PuraTheme`/`PuraColors`/`PuraTypography`/`PuraSpacing`/`PuraCodeTheme`、`PuraLimits` |
| 插件 | `IPuraPlugin`、`IPuraRenderer`、`IIncrementalPuraRenderer`、`IPuraInlineRenderer`、`IDiagramRenderer`、`ICodeHighlighter`、`IAgentComponentParser/Renderer` |
| 流式 | `PuraStreamingSession`、`PuraStreamingOptions`、`PuraStreamingStatistics`、`PuraStreamingState`、`BlockRecord` |
| Agent | `PuraAgentComponent` 及 choice/confirm 模型、`IPuraInteractionStateReceiver`、交互事件、Schema 校验 |
| 安全/平台 | `IUrlPolicy`、`IAstSanitizer`、`IImageLoader`、`ILinkHandler`、`IPuraClipboard`、`IPuraPlatformServices` |
| 测试 | `PuraMarkdown.Testing`（dump/golden/交互探针/流式驱动/插件一致性） |

优势：渲染器/组件/图表/高亮/剪贴板均可替换；流式有同步/异步两种模式；块级/消息级虚拟化都有落点。

---

## 2. 优先修复：公共 API 的一致性/可用性

### P0-1 `IPuraInlineRenderer` 是"死接口"

- 现状：接口公开、注册表有 `ResolveInline`，但 `DefaultInlineRenderer.RenderOne` 从不调用它；插件注册的行内渲染器永远不会生效。
- 建议：在未知 inline 节点的 `default` 分支前解析并调用：
  ```csharp
  var inlineRenderer = context.Renderers.ResolveInline(inline, context);
  if (inlineRenderer is not null)
  {
      target.Add(inlineRenderer.RenderInline(inline, context));
      return;
  }
  ```
  并补测试 + 文档。

### P0-2 缺少 `UseSanitizer`，`IAstSanitizer` 无法替换

- 现状：builder 内部持有 `_sanitizer`，但没有公开方法；第三方无法注册自定义 AST 清洗策略。
- 建议：
  ```csharp
  public PuraPipelineBuilder UseSanitizer(IAstSanitizer sanitizer)
  ```
- 同时把 `SanitizeResult.Block(...)` 从 `internal` 改为公开（或提供 `AddWarning/BlockNode`），否则自定义 sanitizer 无法真正阻断节点。

### P0-3 `UseLimits` 实际不生效

- 现状：`UseLimits` 配置的是 `PuraRenderEnvironment.Limits`，但视图/清洗器读取的是 `PuraMarkdownOptions.Limits`；两者互不相通。
- 建议二选一：
  1. 删除 `UseLimits`/`Environment.Limits`，统一以 `PuraMarkdownOptions.Limits` 为准；
  2. `PuraMarkdownOptions.Limits` 改为可空，未设置时回落到 pipeline 的 limits。
- 推荐 1（单一路径，最少歧义）。

### P0-4 未使用的公开 API

| 成员 | 问题 | 建议 |
| --- | --- | --- |
| `PuraMarkdownOptions.Logger` | 从未使用 | 接入警告/渲染异常日志，或删除 |
| `PuraStreamingOptions.EnableBlockReuse` | 从未使用 | 实现块复用开关，或删除 |
| `IPuraPlatformServices` / `DefaultPlatformServices` | 未被 options 使用 | 增加 `PuraMarkdownOptions.PlatformServices` 并让 Clipboard 统一走它，或删除 |
| `PuraRenderEnvironment.Limits` | 无消费方 | 删除（随 P0-3） |

### P0-5 视图缺少会话/生命周期 API

- 现状：`BeginStreaming` 返回 session，但视图不暴露 `ActiveSession/IsStreaming`；宿主丢失 session 后无法 Abort/Complete；视图也未实现 `IDisposable`。
- 建议：
  ```csharp
  public PuraStreamingSession? ActiveSession { get; }
  public bool IsStreaming { get; }
  public void AbortStreaming(); // 或 Task AbortAsync
  ```
  并在 `Unloaded`/Dispose 时取消后台 worker。

---

## 3. 流式公共 API 的补充

| API | 用途 |
| --- | --- |
| `AppendAsync(ReadOnlyMemory<char>, CancellationToken)` | 宿主异步 token 流，天然背压 |
| `FlushAsync()` / `WaitForIdleAsync()` | 测试、截图、导出前强制完成当前合帧 |
| `event Completed` / `Aborted` | 宿主不需要保存/等待 Task |
| `CompleteAsync(CancellationToken)` | 超长文档可取消 |
| `PendingChars` / `LastWorkerError` 事件 | 诊断与 UI 状态 |
| `PuraStreamingOptions.AutoScrollToEnd` | 聊天自动跟随（当前需宿主自己调 `ScrollToEnd`） |

> 现有 `LastWorkerError` 已可用于诊断，但建议同时通过 `Warning` 事件上报（本轮已做 `PURA-STREAM-001`）。

---

## 4. 宿主集成/聊天层

### 4.1 消息模型与聊天控件

当前 Demo 的 `VirtualizedChatWindow` 是示例；若希望"开箱即用聊天列表"，建议提供独立包 `PuraMarkdown.Chat`：

```csharp
public sealed class PuraChatMessage
{
    public string Id { get; init; }
    public PuraMessageRole Role { get; init; }
    public string Markdown { get; set; }
    public PuraMessageStatus Status { get; set; } // Streaming/Complete/Error
}

public sealed class PuraChatView : ContentControl
{
    public ObservableCollection<PuraChatMessage> Messages { get; }
    public PuraMarkdownOptions Options { get; init; }
    public PuraStreamingSession BeginStreamingMessage(string? id = null);
    public void ScrollToMessage(string id);
}
```

价值：内置消息级虚拟化、流式消息槽、自动滚动、状态恢复；核心包保持"单控件渲染"职责。

### 4.2 文本选择/复制

- 现状：`TextBlock` 不可选择，用户无法复制消息正文；只有代码块复制按钮。
- 建议：
  - `PuraMarkdownOptions.EnableTextSelection`（使用 `SelectableTextBlock` 或选区宿主）；
  - `PuraMarkdownView.CopyAllAsync()` / `GetPlainText()`；
  - 右键菜单"复制消息/复制代码"。

### 4.3 大纲、锚点与滚动

```csharp
public IReadOnlyList<PuraHeadingInfo> GetOutline();
public bool ScrollToAnchor(string anchor);
```

- 需要 `UseAutoIdentifiers()`（Markdig）或自建 slug；当前标题没有可跳转 ID。
- 对聊天内"跳到某段/引用定位"很有用。

### 4.4 链接/图片事件

- 现状：只能通过 `ILinkHandler`/`IImageLoader` 替换行为；宿主无法只监听点击而不接管打开。
- 建议：
  ```csharp
  public event EventHandler<PuraLinkClickedEventArgs>? LinkClicked;
  public event EventHandler<PuraImageEventArgs>? ImageLoading/ImageLoaded;
  ```
  默认仍调用 `ILinkHandler`，事件可取消（`Handled=true`）。

---

## 5. 可扩展性补充

### 5.1 通用围栏渲染器

`IDiagramRenderer` 只覆盖图表；数学、图表、自定义围栏各建一套接口会碎片化。建议增加通用契约：

```csharp
public interface IFenceRenderer
{
    bool CanRenderFence(string language, string source, RenderContext context);
    Control RenderFence(string language, string source, RenderContext context);
}
```

- `IDiagramRenderer` 作为 `IFenceRenderer` 的兼容适配（或保留但标记旧）；
- 同时可替代 `mermaid`/`math`/`chart` 专用分发；
- 与现有 `DiagramDispatchRenderer` 回退链一致。

### 5.2 Agent 插件辅助 API

- `AgentJson` 目前 `internal`；插件作者需要反复写 `JsonElement` 解析。
- 建议公开：
  ```csharp
  public static class PuraAgentJson
  {
      public static bool TryReadString(...);
      public static bool TryReadBool(...);
      public static bool TryReadInt(...);
  }
  ```
- 提供 `PuraComponentSchema`（JSON Schema 构造/校验）与 `IAgentComponentParser` 默认基类，降低插件门槛。

### 5.3 宿主侧交互状态 API

当前 `IPuraInteractionStateReceiver` 由库内控件实现；宿主无法预置/清空状态。建议：

```csharp
public IReadOnlyDictionary<string, PuraInteractionEventArgs> InteractionStates { get; }
public void SetInteractionState(string componentId, PuraInteractionEventArgs state);
public void ClearInteractionStates();
```

用于：恢复历史会话、测试回放、服务端状态同步。

### 5.4 插件元数据与生命周期

- `IPuraPlugin` 只有 Name/Version/Description/Configure。
- 建议增加 `MinCoreVersion`、`Capabilities`、`Dispose`，并在 `Build()` 做版本兼容检查；对多插件组合（高亮+图表+自定义组件）更可控。

---

## 6. Markdown 内容能力补充

| 能力 | 建议 |
| --- | --- |
| 脚注 | 增加 `UseFootnotes()`（Markdig 扩展） |
| 定义列表 | 增加 `UseDefinitionLists()` |
| GitHub Alerts（`> [!NOTE]`） | 自定义 Markdig 扩展 + 主题色（note/tip/warning/important/caution） |
| Emoji shortcode | 可选 `UseEmoji()`；或保持纯文本 |
| 数学公式 | 通过 `IFenceRenderer`/`IMathRenderer` 插件（不进入核心依赖） |
| 标题锚点/TOC | 见 4.3 |
| 代码块选项 | `WrapLines`、`ShowLineNumbers`、`CopyButton`、语言别名映射 |
| 图片 | `MaxImageBytes`/超时/取消、最大显示尺寸、点击放大（lightbox 可宿主做） |
| 表格 | 列宽策略、粘性表头、空单元格占位（已补） |
| 引用容器 `:::` | 维持插件方案；可公开 `ICustomContainerRenderer`（需评估与 ADR-0012 的一致性） |

---

## 7. 主题、A11y、国际化

### 7.1 主题实时切换

- `FollowSystemTheme` 在渲染时解析，但没有监听 `ActualThemeVariantChanged`；系统切深色不会自动重渲染。
- 建议：重写 `OnActualThemeVariantChanged` 或在 `PuraMarkdownView` 订阅，触发 `Render()`；提供 `RefreshTheme()`。
- 主题 token 建议增加 `Scale`/`Density`、高对比度主题、代码 token 更细分类（类型/函数/标点）。

### 7.2 本地化

当前 UI 文案与 warning 消息为中文硬编码（"复制""已复制""组件加载中…"等）。作为公共库应支持：

```csharp
public interface IPuraStringLocalizer
{
    string Get(string key, params object[] args);
}
public PuraMarkdownOptions.Strings { get; init; } // default zh/en?
```

价值：非中文宿主、日志/诊断、测试断言不依赖中文文案。

### 7.3 A11y 补充

- 表格/列表的 Automation 语义；
- 代码块复制按钮已补 Name；
- live region 已实现；
- 建议增加 `AutomationProperties` 的 heading/outline 导航测试、读屏手工走查。

---

## 8. 导出与诊断

```csharp
public string GetPlainText();                 // 无障碍/复制/搜索
public IReadOnlyList<PuraHeadingInfo> GetOutline();
public PuraRenderStatistics LastRenderStats; // 块数、耗时、复用数
```

- `GetPlainText` 可复用 AST，不需要 IR；
- 对聊天搜索、导出、测试很有价值；
- `PuraStreamingStatistics` 已有，建议也提供视图级渲染统计。

---

## 9. 建议的优先级

| 优先级 | 内容 |
| --- | --- |
| **P0（先修 API 正确性）** | `IPuraInlineRenderer` 接线、`UseSanitizer`、统一 limits、去掉/接入未使用 API、视图会话/Dispose |
| **P1（宿主集成）** | 流式 `AppendAsync/Flush/Completed`、文本选择/复制、大纲/锚点、链接事件、本地化 |
| **P1（可扩展性）** | 通用 `IFenceRenderer`、Agent JSON/状态公开 API、插件元数据 |
| **P2（内容语法）** | 脚注、定义列表、GitHub Alerts、Emoji、代码块选项、图片预算/lightbox |
| **P2（包级）** | `PuraMarkdown.Chat` 聊天控件、API baseline/SemVer 检查、插件市场文档 |

---

## 10. 结论

- 当前 API 已覆盖"渲染 + 插件 + 流式 + 虚拟化"，但**扩展点存在死接口/不可配置项**（P0 应先修）。
- 对宿主价值最高的新增是：**流式生命周期 API、文本选择/复制、大纲/锚点、链接事件、本地化**。
- 对生态价值最高的是：**通用围栏渲染器 + Agent 插件辅助 API + 插件元数据**。
- 对聊天产品价值最高的是：**独立 `PuraMarkdown.Chat` 包**（消息模型 + 虚拟化列表 + 流式槽）。

