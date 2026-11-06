# PuraMarkdown 架构设计（v0.3）

> 状态：草案 v0.3（按 [`PuraMarkdown需求方案.md`](PuraMarkdown需求方案.md) 重构；对齐结论见 [`00-需求对齐与设计复审.md`](00-需求对齐与设计复审.md)）
> 技术前提：**.NET 10（net10.0）**、**Avalonia 12.1.5**、**Markdig 1.5.1**；渲染器直接产出 Avalonia 控件，**不再有中间 IR 与多平台适配层**。
> 关联：[需求规格](./01-requirements.md) · [Agent 扩展语法规范](./03-agent-syntax-spec.md) · [插件与渲染契约](./04-plugin-and-rendering.md) · [路线图与风险](./05-roadmap-and-risks.md)

---

## 1. 范围与设计目标

PuraMarkdown 是一个 **Avalonia 只读 Markdown 控件**，用于渲染 Agent 对话内容：

- 输入：Markdown 文本（可全量，也可 `Append` 流式追加）；
- 输出：Avalonia 原生视觉树（`Control`）；
- 扩展：插件可替换任意"可渲染节点"的渲染器，可注册新的 Agent 组件类型、高亮器、图表渲染器；
- 交互：仅 Agent 交互组件（单选/多选/确认）与查看类交互（链接、复制）；不编辑、不管理对话、不发送消息。

### 1.1 设计原则

| # | 原则 | 含义 |
| --- | --- | --- |
| P1 | **只有一个模型** | Markdig AST 就是文档模型；不引入 IR、不做 AST→IR→RenderNode 的两级映射 |
| P2 | **分发表驱动** | 节点 → 渲染器查注册表；基础渲染器只是"低优先级默认项"，不是特权路径 |
| P3 | **契约窄** | 插件契约只覆盖解析、渲染、Agent 组件、图表、高亮五类；不引入编辑契约 |
| P4 | **失败可读** | 解析失败/渲染失败/不完整语法 → 可读降级内容 + 非致命通知，绝不白屏或崩溃 |
| P5 | **默认开箱可用** | 不装任何插件也能正确渲染标准 Markdown；插件是增强，不是前置条件 |
| P6 | **流式优先** | 追加路径是一等 API，块级增量渲染；未闭合语法不报错 |
| P7 | **安全默认** | 不渲染 HTML、URL 白名单、零网络请求、解析预算上限 |

### 1.2 明确不做的

- 源码感知编辑、源码位置命中测试、编辑会话（属于编辑器控件的领域）；
- 对话状态、消息历史、Agent 通信协议（属于宿主）；
- 内置图表引擎、内置语法高亮引擎（外置为插件）；
- 多平台抽象（1.0 只做 Avalonia，见 ADR-0016）。

---

## 2. 总体架构：四层

```
┌──────────────────────────────────────────────────────────────────┐
│ 宿主应用（Avalonia 窗口 / Agent 客户端）                          │
│   <pura:PuraMarkdownView Markdown="{Binding Text}"                │
│                          Options="{StaticResource PuraDefaults}"  │
│                          InteractionOccurred="OnChoice" />        │
└───────────────────────────┬──────────────────────────────────────┘
                            │ Markdown 文本 / Append 增量
┌───────────────────────────▼──────────────────────────────────────┐
│ ① 解析路由层（Parse & Route）                                     │
│   Markdig Pipeline（CommonMark+GFM, PreciseSourceLocation,        │
│   DisableHtml） + 自定义 BlockParser/InlineParser                 │
│   → 混合 AST（标准节点 + PuraAgentBlock + 自定义节点）            │
└───────────────────────────┬──────────────────────────────────────┘
                            │ MarkdownObject
┌───────────────────────────▼──────────────────────────────────────┐
│ ② 节点分发层（Node Dispatch）                                     │
│   渲染器注册表：按优先级找第一个 CanRender(node, ctx) == true      │
│   命中插件渲染器 → 插件渲染器；否则 → 基础默认渲染器              │
└───────────────────────────┬──────────────────────────────────────┘
                            │ IPuraRenderer.Render(...)
┌───────────────────────────▼──────────────────────────────────────┐
│ ③ 渲染执行层（Render → Avalonia）                                 │
│   基础渲染器：TextBlock / Border / StackPanel / Grid / Image ...   │
│   插件渲染器：任意 Avalonia Control（图表、交互列表、自定义）      │
│   块宿主：按块组织视觉树，支持增删替换                             │
└───────────────────────────┬──────────────────────────────────────┘
                            │ Control / 增量嫁接
┌───────────────────────────▼──────────────────────────────────────┐
│ ④ 流式会话层（Streaming Session）                                 │
│   Append（线程安全）→ 稳定边界扫描 → 尾部窗口重解析 → 块级 diff   │
│   → UI 线程嫁接；未闭合围栏显示占位                                │
└──────────────────────────────────────────────────────────────────┘
```

### 2.1 各层职责与边界

| 层 | 输入 | 输出 | 不做什么 |
| --- | --- | --- | --- |
| ① 解析路由 | Markdown 文本 | Markdig AST（含 `PuraAgentBlock`） | 不判断"怎么画"，不做 schema 校验（交给 ② 的 Agent 组件解析器） |
| ② 节点分发 | Markdig 节点 | 选中的 `IPuraRenderer` | 不解析 Markdown、不构造控件 |
| ③ 渲染执行 | 节点 + `RenderContext` | `Control`（或 `Inline`） | 不发网络请求、不管理对话、不写回文本 |
| ④ 流式会话 | 文本增量 | 增量视觉树更新 | 不做编辑/重写；不做字符级源码映射 |

---

## 3. 公共 API 与生命周期

### 3.1 控件

```csharp
namespace PuraMarkdown;

public sealed class PuraMarkdownView : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty;
    public static readonly StyledProperty<PuraMarkdownOptions?> OptionsProperty;

    /// 全量文本（变更时整篇重渲染，适合历史消息）
    public string? Markdown { get; set; }
    public PuraMarkdownOptions? Options { get; set; }

    /// 交互事件（选择/确认）；库只上报，不决定后续行为
    public event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;

    /// 非致命问题（解析/渲染降级、插件异常、资源拒绝）
    public event EventHandler<PuraRenderWarningEventArgs>? Warning;

    /// 打开流式会话（同一控件同一时刻只允许一个活动会话）
    public PuraStreamingSession BeginStreaming(PuraStreamingOptions? options = null);
}
```

### 3.2 流式会话（append-only）

```csharp
public sealed class PuraStreamingSession : IAsyncDisposable
{
    public PuraStreamingState State { get; }        // Created / Streaming / Completed / Aborted / Faulted
    public void Append(string delta);               // 任意线程可调用，内部串行化
    public Task CompleteAsync(CancellationToken ct = default);
    public Task AbortAsync();                        // 停止接收，保留已渲染内容（终态，不可续写）
    public ValueTask DisposeAsync();
}
```

> 状态机：`Created → Streaming → Completed | Aborted | Faulted`，终态不可逆（ADR-0010）。

### 3.3 选项与管道

```csharp
public sealed class PuraMarkdownOptions
{
    public required PuraPipeline Pipeline { get; init; }
    public PuraTheme Theme { get; init; } = PuraTheme.FluentLight;
    public bool InteractionEnabled { get; init; } = true;      // 只读模式可关
    public IImageLoader ImageLoader { get; init; } = NullImageLoader.Instance;
    public ILinkHandler LinkHandler { get; init; } = DefaultLinkHandler.Instance;
    public ILogger? Logger { get; init; }
    public bool EnableBlockVirtualization { get; init; }        // M3 验证后默认 false
}

public sealed class PuraPipelineBuilder
{
    // 解析
    public PuraPipelineBuilder UseCommonMark();
    public PuraPipelineBuilder UseGfm();
    public PuraPipelineBuilder UseMarkdownExtension(IMarkdownExtension extension);   // 第三方语法
    public PuraPipelineBuilder UseAgentComponents();                                  // pura-agent + 内置 choice/confirm
    public PuraPipelineBuilder UseAgentComponentParser(IAgentComponentParser parser);
    public PuraPipelineBuilder UseAgentComponentRenderer(IAgentComponentRenderer renderer, int priority = 100);

    // 渲染
    public PuraPipelineBuilder UseRenderer(IPuraRenderer renderer, int priority = 100);
    public PuraPipelineBuilder UseInlineRenderer(IPuraInlineRenderer renderer, int priority = 100);
    public PuraPipelineBuilder UseDiagramRenderer(IDiagramRenderer renderer);
    public PuraPipelineBuilder UseCodeHighlighter(ICodeHighlighter highlighter);

    // 运行环境
    public PuraPipelineBuilder UseUrlPolicy(IUrlPolicy policy);
    public PuraPipelineBuilder UseLimits(Action<PuraLimits> configure);
    public PuraPipelineBuilder UsePlugin(IPuraPlugin plugin);

    public PuraPipeline Build();
}
```

### 3.4 典型用法

```csharp
// 应用启动时构建一次（进程级复用）
var pipeline = new PuraPipelineBuilder()
    .UseCommonMark()
    .UseGfm()
    .UseAgentComponents()
    .UseRenderer(new MermaidDiagramPlugin(), priority: 200)   // 或 UsePlugin(...)
    .UseCodeHighlighter(new ColorCodeHighlighter())          // 可选
    .UseLimits(l => l.MaxDocumentChars = 4 * 1024 * 1024)
    .Build();

var options = new PuraMarkdownOptions { Pipeline = pipeline, Theme = PuraTheme.FluentDark };
var view = new PuraMarkdownView { Options = options };

// 历史消息：整篇
view.Markdown = message.Text;

// 流式消息
var session = view.BeginStreaming();
session.Append(chunk);
await session.CompleteAsync();
```

---

## 4. 解析路由层

### 4.1 管道

- Markdig `MarkdownPipelineBuilder`：`UseAdvancedExtensions()` 按需裁剪，固定 `UsePreciseSourceLocation()`（否则行内 `Span` 全为 0，流式定位失效）、`UseGfm` 等价配置（PipeTables/TaskLists/AutoLinks/EmphasisExtras）、`DisableHtml()`。
- `MarkdownPipeline` 不可变、线程安全，进程级单例；每次解析创建独立 parse 调用（`Markdown.Parse(text, pipeline)`）。
- 输入规范化：去 BOM、统一 LF（Markdig 不剥离 BOM，实测会吞掉首个标题）。

### 4.2 `pura-agent` 语法识别

`PuraAgentBlockParser : BlockParser` 拦截 **info 为 `pura-agent` 的围栏代码块**：

```
```pura-agent
{ "type": "choice", "version": "1.0", "id": "env", "question": "选择部署环境", "multi": false, "options": [...] }
```
```

- 识别规则：行首（≤3 空格）连续 ``` 或 ~~~ ≥ 3 个，info 去除空白后等于 `pura-agent`（大小写不敏感）。
- 产出 `PuraAgentBlock : LeafBlock`，携带：
  - `RawJson`：围栏内原文；
  - `IsClosed`：是否已出现闭合围栏（流式时用于判断 `Incomplete`）；
  - `Language`/`Info` 原文、`Span`。
- **不在解析层做 JSON 解析**：JSON→组件模型交给 ② 的 `IAgentComponentParser`，使"解析失败"可以走组件级降级，而不污染 Markdig AST。

其它围栏（`mermaid`、`pura-chart`、未知语言）保持标准 `FencedCodeBlock`，由分发层决定是走 `IDiagramRenderer` 还是代码块渲染器。

### 4.3 混合 AST 与不完整标记

- 标准节点：`ParagraphBlock`、`HeadingBlock`、`ListBlock`、`QuoteBlock`、`FencedCodeBlock`、`Table`、`ThematicBreak` 等；
- 扩展节点：`PuraAgentBlock`；插件通过自己的 `BlockParser` 可加入任意 `Block`/`Inline`；
- 不完整：`PuraAgentBlock.IsClosed == false` 或尾部围栏未闭合 → 分发层渲染占位（"组件加载中…"），不产生错误。

### 4.4 安全（解析期）

| 风险 | 处理 |
| --- | --- |
| 原始 HTML | `DisableHtml()`；HTML 节点按**转义文本**渲染（ADR-0009），无 HTML 子集/清洗器 |
| 解析预算 | 文档字符上限、单围栏字节上限、嵌套深度、组件数量上限；超限 → 截断/降级 + warning |
| BOM/CRLF | 解析前规范化 |
| 恶意输入 | Markdig 本身健壮；额外用模糊测试语料回归 |

> 关键事实（实测）：`DisableHtml()` **不过滤 URL scheme**（`javascript:` 原样进 `href`），URL 策略必须在渲染期执行（§11）。

---

## 5. 节点分发层

### 5.1 渲染器契约

```csharp
namespace PuraMarkdown.Rendering;

public interface IPuraRenderer
{
    /// 必须廉价、无副作用、确定性；返回 true 表示由本渲染器负责该节点
    bool CanRender(MarkdownObject node, RenderContext context);

    /// 在 UI 线程调用；返回该节点的 Avalonia 控件
    Control Render(MarkdownObject node, RenderContext context);
}

/// 可选：支持"同一节点类型、内容变化"的就地更新，用于流式尾部块与交互状态
public interface IIncrementalPuraRenderer : IPuraRenderer
{
    bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context);
    void Update(Control existing, MarkdownObject newNode, RenderContext context);
}

/// 行内扩展节点的渲染器（返回 Avalonia Inline，可放入 TextBlock.Inlines）
public interface IPuraInlineRenderer : IPuraRenderer
{
    Inline RenderInline(MarkdownObject node, RenderContext context);
}
```

### 5.2 解析规则与优先级

- 注册表保存 `(IPuraRenderer renderer, int priority)`；解析时按 **priority 降序** 遍历，首个 `CanRender == true` 胜出。
- 基础默认渲染器由核心以 **priority 0** 注册；插件推荐 **≥ 100**；需要"覆盖内置"时用更高优先级 + 更具体的 `CanRender`。
- `CanRender` 应优先判断节点类型（`node is PuraAgentBlock`），避免在每个节点上做昂贵检查。
- 冲突处理：同优先级按注册顺序（后者覆盖前者会产生 warning）；`Build()` 时校验重复注册（同类型同优先级）并报告。

### 5.3 内联处理

- 默认段落渲染器负责把 `ContainerInline` 子节点转为 `TextBlock.Inlines`；遇到注册了 `IPuraInlineRenderer` 的自定义节点时，调用其 `RenderInline` 并放入 `InlineUIContainer` 或转换为 `Run`。
- 链接/图片/代码等标准行内由默认渲染器统一处理，保证 URL 策略与图片加载策略集中。

### 5.4 覆盖示例

```csharp
// 把默认代码块替换为自带高亮的控件
public sealed class HighlightedCodeRenderer : IPuraRenderer
{
    public bool CanRender(MarkdownObject node, RenderContext ctx)
        => node is FencedCodeBlock or CodeBlock;

    public Control Render(MarkdownObject node, RenderContext ctx) { ... }
}

new PuraPipelineBuilder().UseRenderer(new HighlightedCodeRenderer(), priority: 300);
```

---

## 6. 渲染执行层

### 6.1 默认渲染组（可覆盖，priority 0）

| Markdig 节点 | 默认控件 | 说明 |
| --- | --- | --- |
| `ParagraphBlock` | `SelectableTextBlock`（可配置为 `TextBlock`） | 支持文本选择/复制；行内由 `Inlines` 组合 |
| `HeadingBlock` | `TextBlock` | 按 `Level` 应用主题字号/字重；生成稳定 `AutomationProperties.HeadingLevel` |
| `ListBlock`/`ListItemBlock` | `Grid`/`StackPanel` | 项目符号/编号由 `ListBlock.IsOrdered`/`OrderedStart` 决定；任务列表用只读 `CheckBox` |
| `QuoteBlock` | `Border`（左侧竖线）+ 子块容器 | 递归渲染子块 |
| `FencedCodeBlock`/`CodeBlock` | `CodeBlockControl` | 语言标签 + 复制按钮 + 等宽文本；高亮经 `ICodeHighlighter` 注入 |
| `Table` | `Grid` | 表头/对齐/列宽；超宽时外层 `ScrollViewer` |
| `ThematicBreakBlock` | `Border`（1px 线） | 主题色 |
| `HtmlBlock`/`HtmlInline` | `TextBlock`（转义原文） | 不执行、不渲染 HTML |
| `LinkInline` | `HyperlinkButton`/带下划线的 `TextBlock` | 点击经 `ILinkHandler`，URL 经 `IUrlPolicy` |
| `ImageInline` | `Image`/占位 `Border` | 经 `IImageLoader` 异步加载；默认显示占位 + 域名 |
| `EmphasisInline`/`CodeInline` 等 | `Run` 样式 | 字体/背景/颜色来自主题 token |
| `PuraAgentBlock` | 组件渲染器产物 | 见 §7 |
| `FencedCodeBlock`（mermaid/pura-chart） | `IDiagramRenderer` 产物 | 无插件 → 回退代码块（见 §8） |

### 6.2 块宿主与视觉树组织

- 每个**顶层块**对应一个子控件，统一放入块宿主（默认纵向 `StackPanel`，`ItemsControl` 可替换以支持虚拟化）。
- 宿主维护 `BlockViewRecord { Key, MarkdigObject, Control }` 列表；这是流式增量嫁接的锚点。
- 块级粒度的好处：追加只影响尾部；交互组件状态不随其他块重渲染而丢失；错误隔离以块为单位。

### 6.3 代码块控件

- 组成：`Border`（背景/圆角）+ 头部（语言标签、复制按钮）+ 内容区。
- 默认内容：`SelectableTextBlock`，`FontFamily` 使用主题等宽字体，自动换行关闭 + 横向滚动。
- 高亮：`ICodeHighlighter.Highlight(code, language)` 返回 token span；渲染器据此构造彩色 `Run`；高亮失败 → 纯文本 + warning。
- 复制：`TopLevel.Clipboard`；复制失败 → warning + 用户可见提示（ToolTip）。

### 6.4 主题

```csharp
public sealed class PuraTheme
{
    public required string Name { get; init; }
    public required PuraColors Colors { get; init; }
    public required PuraTypography Typography { get; init; }
    public required PuraSpacing Spacing { get; init; }
    public PuraCodeTheme CodeTheme { get; init; }
    public ImmutableDictionary<string, object> Extras { get; init; }
}
```

- 内置 `FluentLight` / `FluentDark`，跟随系统主题（`Application.RequestedThemeVariant`）。
- 主题切换：重新渲染所有块（IR 不存在，无法只换样式 token）；对历史消息可延迟到可见时重渲染。
- 插件渲染器通过 `RenderContext.Theme` 取 token，不得硬编码颜色。

### 6.5 图片与链接

```csharp
public interface IImageLoader
{
    Task<IImageResult> LoadAsync(string url, CancellationToken ct);   // Success(Bitmap) / Refused(reason) / Failed
}
public interface ILinkHandler
{
    Task OpenAsync(string url, CancellationToken ct);                 // 宿主决定：浏览器/内置视图/禁止
}
public interface IUrlPolicy
{
    UrlDecision Evaluate(string url, UrlUsage usage);                 // Allow / Block / EscapeAsText
}
```

- 默认 `NullImageLoader`：不联网，渲染"图片占位 + 域名 + alt"。
- 默认 `DefaultLinkHandler`：仅允许 `http/https/mailto`，其余按文本显示；不自动打开浏览器。
- 图片加载超时/失败 → 占位 + warning；解码在后台线程，赋值 `Image.Source` 在 UI 线程。

---

## 7. Agent 组件

### 7.1 从围栏到模型

```
FencedCodeBlock(info="pura-agent")
   → PuraAgentBlockParser 拦截 → PuraAgentBlock(rawJson, isClosed)
   → 分发层：核心 AgentComponentRenderer（priority 100）接管
   → 读取 JSON.type → 找 IAgentComponentParser(type) → JSON Schema 校验 → PuraAgentComponent 模型
   → 按 type 找 IAgentComponentRenderer → 渲染为 Avalonia 控件（choice/confirm/自定义）
   → 任何一步失败（未知 type / JSON / schema）→ 核心统一降级卡（不交给插件）
```

### 7.2 组件模型与解析器

```csharp
public abstract record PuraAgentComponent
{
    public required string Type { get; init; }        // "choice" | "confirm" | 插件自定义
    public required string Version { get; init; }     // 载荷 schema 版本，如 "1.0"
    public string? Id { get; init; }                  // 组件实例 ID（缺省由块位置生成）
}

public interface IAgentComponentParser
{
    string ComponentType { get; }                     // "choice"
    JsonSchema PayloadSchema { get; }
    bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error);
}

/// 组件渲染器：解析成功后由核心按 ComponentType 查找并调用
public interface IAgentComponentRenderer
{
    string ComponentType { get; }                     // "choice" / 插件自定义
    Control RenderComponent(PuraAgentComponent component, PuraAgentBlock node, RenderContext context);
}
```

- 解析顺序：`type` 字段 → 注册的 `IAgentComponentParser`；未知 type → 通用降级卡（显示原文 + "不支持的组件类型"）。
- 校验使用 `JsonSchema.Net`；失败返回**第一条**错误（JSON Pointer + 原因），用于降级卡展示。
- 上限：单围栏 ≤ 256 KB、options ≤ 200、字符串长度上限；超限直接降级，不进入 schema 校验。

### 7.3 默认 `choice` 组件

```json
{
  "type": "choice",
  "version": "1.0",
  "id": "env",
  "question": "选择部署环境",
  "multi": false,
  "options": [
    { "id": "dev", "label": "开发", "description": "快速迭代" },
    { "id": "prod", "label": "生产", "description": "需要审批", "disabled": true }
  ],
  "defaultSelected": ["dev"],
  "min": 1,
  "max": 1,
  "submitLabel": "确认"
}
```

- 渲染：`Border` + 标题 + 选项列表（单选 `RadioButton`，多选 `CheckBox`）+ 可选提交按钮。
- 状态：`未选 / 选中 / 已提交 / 禁用（只读或流式未完成）/ 错误`。
- 上报时机（Q2 已确认）：单选点击即上报；多选点击"确认"后上报；`Options.InteractionEnabled=false` 时全部禁用、不上报。
- 流式期间：`PuraAgentBlock.IsClosed == false` → 占位控件；闭合后若 JSON 解析成功 → 替换为正式控件；解析失败 → 降级卡。

### 7.4 交互事件

```csharp
public sealed record PuraInteractionEventArgs
{
    public required string ComponentType { get; init; }
    public string? ComponentId { get; init; }
    public required IReadOnlyList<string> SelectedIds { get; init; }   // choice
    public bool? Confirmed { get; init; }                              // confirm
    public DateTimeOffset Timestamp { get; init; }
}
```

- 库只触发 `InteractionOccurred`；宿主决定"发回 Agent / 禁用 / 记日志"。
- 回传安全：只回传已解析组件模型中的选项 ID，不接受控件上的任意文本。
- 交互组件在流式结束前禁用（避免选项未输出完就被点击）。

### 7.5 只读/禁用与降级

- `InteractionEnabled=false`：控件 `IsEnabled=false`，展示当前选中态（若有），不触发事件。
- 载荷非法：`Border` 内显示"组件解析失败：`/options/1/id: required`" + 原始 JSON（可滚动、可复制）。
- 未知类型：显示"不支持的组件类型：xxx" + 原始 JSON。

---

## 8. 图表、高亮、公式：外部契约

| 扩展 | 契约 | 默认行为 | 官方插件（可选） |
| --- | --- | --- | --- |
| 图表/图形 | `IDiagramRenderer.CanRenderDiagram(language, source)` / `RenderDiagram(...)` | 回退代码块（可复制源码） | `PuraMarkdown.Diagrams.Mermaid`（Mermaider/Naiad 之一，M2 spike 定） |
| 代码高亮 | `ICodeHighlighter.CanHighlight(language)` / `Highlight(code, language)` | 纯等宽文本 | `PuraMarkdown.Highlighting.ColorCode`（ColorCode.Core 2.0.15）或 TextMate 版 |
| 公式 | 插件注册 `IPuraRenderer` 处理 `$$...$$`/`$...$` 节点（或自定义 BlockParser） | 按文本显示 TeX 源码 | `PuraMarkdown.Math.CSharpMath`（1.1 候选） |

- 核心**不定义** `ChartSpec`；图表数据格式属于插件（`pura-chart` 的 schema 由插件包提供）。
- 分发顺序：对 `FencedCodeBlock`，先问所有 `IDiagramRenderer`（`CanRenderDiagram`），命中则渲染；否则走代码块渲染器。
- 插件不得在 `CanRender`/`Render` 中发起同步网络请求；异步资源通过 `IImageLoader`/宿主服务注入。

---

## 9. 流式会话

### 9.1 为什么不能"只重解析最后一个块"

追加文本时，最后一个块可能是：

- 普通段落（追加只改它）→ 简单；
- 未闭合围栏/`pura-agent`（内容持续增长）→ 不能提交；
- 列表/引用/容器（空行后仍可能续接）→ 最后一个块不一定"终结"；
- 表格（缺分隔行时会退化）→ 需要更多输入才能确定。

因此采用 **"已提交前缀 + 尾部窗口"**：只有确认稳定的块才提交，尾部窗口（默认 8–16 KB）每次重解析。这与提案的"块级脏检查"一致，但补上了正确性边界。

### 9.2 状态机与数据

```
StreamingSession:
  _text: StringBuilder            // 全文（用于 span 与最终解析）
  _committedOffset: int           // 已提交前缀的字符偏移
  _records: List<BlockRecord>     // 已提交块：{ Hash, Start, End, Control }
  _tailControl: Control?          // 未提交尾部的控件（占位/半成品）
```

`BlockRecord.Hash = SHA256(块源码文本)`（或 xxHash），用于判断"内容是否变化"，不依赖 Markdig 对象身份。

### 9.3 追加流程

```
Append(delta):
  1. 状态检查（仅 Streaming 可写）；_text.Append(delta)；唤醒后台解析任务（合并 16ms 内的多次 Append）
后台解析（Task）:
  2. cut = FindStableBoundary(_text, from: _committedOffset)
       - 顶层空行且不在围栏/容器内
       - 围栏（``` / ~~~ / pura-agent）必须闭合
       - 表格需出现非表格行
       - 列表/引用：连续缩进可能续接 → 保守判定，宁可留在尾部
       - 引用定义：全文预扫描 `[id]: url`，拼接到窗口文本前参与解析
  3. 解析 _text[_committedOffset..cut]（Markdig.Parse）→ 得到稳定块列表
  4. 对每个稳定块：Hash 与 _records 比较；新块 → 渲染控件（UI 线程）
  5. 尾部 _text[cut..] 解析 → 尾部块（不提交）→ 渲染最后一个尾部控件（占位或部分内容）
UI 线程:
  6. 将新块控件追加到块宿主；替换/更新尾部控件（优先 IIncrementalPuraRenderer.Update）
  7. 更新滚动位置（跟随策略：Follow / Anchor / Manual）
```

- 合并：多次 `Append` 在后台只做一次解析（用 `Channel` + 16ms 合帧），避免每 token 一次解析。
- 取消：`AbortAsync`/`DisposeAsync` 取消后台任务；已渲染内容保留，状态转 `Aborted`。
- `CompleteAsync`：解析剩余全部文本 → 提交所有块 → 解析 `pura-agent` 组件 → 最终嫁接 → 状态 `Completed`。

### 9.4 线程模型

| 操作 | 线程 | 说明 |
| --- | --- | --- |
| `Append` | 任意线程 | 内部锁 + Channel；单写入者契约（并发调用串行化） |
| 解析/边界扫描/哈希 | 后台线程 | 不触碰 Avalonia 对象 |
| `Render`/控件创建/嫁接 | UI 线程（`Dispatcher.UIThread`） | 所有 Avalonia 操作 |
| 图片加载 | 后台 + UI 赋值 | `IImageLoader` 异步，`Image.Source` 在 UI 线程 |
| 事件回调 | UI 线程 | 宿主处理器应快速返回 |

### 9.5 不完整语法

| 状态 | 行为 |
| --- | --- |
| 未闭合围栏（代码/`pura-agent`） | 尾部块标记 `Incomplete`；`pura-agent` 显示"组件加载中…"占位；代码块显示半成品文本 |
| 半截 JSON | 不抛错；组件解析延后到围栏闭合；解析失败时降级卡 |
| 未闭合强调/行内代码 | Markdig 显示为字面量；尾部块延迟一帧渲染，完成时校正 |
| `Complete` 时仍不闭合 | 按最终文本尽力解析；仍失败 → 降级为代码块 + warning |

### 9.6 交互组件与流式

- 流式期间组件默认禁用；`Complete` 后启用。
- 若宿主希望"尽早交互"，可设 `PuraStreamingOptions.EnableInteractionWhenComplete` 的反面（默认关闭）；不建议在选项未输出完时允许提交。
- 交互状态保存在组件渲染器产出的控件内；块被替换（如修复解析）时状态丢弃并重置（可接受，因为流式结束后不再替换）。

---

## 10. 主题、可访问性、i18n

- 主题 token：颜色（文本/背景/边框/强调/状态）、字体（正文/等宽/标题）、间距、圆角、代码配色。
- A11y：标题 `HeadingLevel`、列表语义、表格行列、代码块 `AutomationProperties.Name`、交互组件的 `RadioButton`/`CheckBox` 原生语义、流式容器 `LiveSetting=Polite`（完成时播报一次，不逐字）。
- 键盘：Tab 进入组件、方向键在选项中移动、空格选择、Enter 提交；焦点样式来自主题。
- i18n：控件内文案（"复制""组件加载中…""解析失败"）通过 `PuraStrings` 资源覆盖；1.0 提供 zh-CN/en-US；组件文案由载荷提供（Agent 生成的语言）。

### 10.1 跨平台与平台差异服务

跨平台由 **Avalonia 12.1.5** 承担（Windows / macOS / Linux；1.0 不承诺移动端与 WebAssembly）。不做渲染适配层（ADR-0022），只把"平台能力差异"收进薄接口：

| 能力 | 接口 | 默认实现 |
| --- | --- | --- |
| 剪贴板 | `IPuraClipboard` | Avalonia `TopLevel.Clipboard` |
| UI 线程 | `IDispatcher`（Avalonia 类型） | `Dispatcher.UIThread` |
| 图片加载/解码 | `IImageLoader` | `NullImageLoader`（不联网，占位） |
| 链接打开 | `ILinkHandler` | 不自动打开（显示 URL） |
| 无障碍播报 | `IAccessibilityAnnouncer`（可选） | Avalonia AutomationPeer |
| 字体/等宽回退 | `PuraTheme.Typography` + 平台字体探测 | Avalonia 默认字体链 |

> 这些是"服务注入"，不是渲染适配层：它们不抽象控件，只抽象外部能力；未来若做多平台，只需替换实现。

---

## 11. 安全模型

| 层 | 措施 |
| --- | --- |
| 解析 | `DisableHtml()`；BOM/LF 规范化；文档/围栏/嵌套上限 |
| 分发 | 未知节点 → 默认渲染器或纯文本；插件在注册时校验（清单 + 版本） |
| 渲染-文本 | 所有文本以 `Run`/`TextBlock.Text` 绑定，禁止字符串拼接 XAML/HTML |
| 渲染-URL | `IUrlPolicy`：`http/https/mailto` + 锚点；`javascript:`/`vbscript:`/`file:` 拒绝并降级为文本；`data:` 默认拒绝 |
| 渲染-资源 | `IImageLoader` 默认不联网；宿主实现需域名白名单、大小/超时限制 |
| 交互 | 只回传组件模型中已声明的选项 ID；组件类型/id 来自解析结果；无任意字段 |
| 插件 | 显式注册、完全信任（不做沙箱）；禁止从 LLM 输出/网络动态加载程序集 |
| 诊断 | 默认零遥测；warning 不回传全文（可脱敏） |

> 因为不再有 IR 集中过滤层，**URL 策略与 HTML 转义必须在默认渲染器中强制执行**；自定义渲染器若绕过（例如直接 `Process.Start(url)`），由插件作者负责，文档明确警示。

---

## 12. 错误处理与降级链

| 场景 | 行为 |
| --- | --- |
| Markdig 解析异常（理论不应发生） | 整篇降级为纯文本 `TextBlock` + warning |
| `pura-agent` JSON 语法错误 / schema 失败 | 组件降级卡：错误摘要（第一处错误）+ 原始 JSON；`Warning` 事件 |
| 未知组件 type | "不支持的组件类型"卡 + 原始 JSON |
| `IPuraRenderer.Render` 抛异常 | 捕获 → warning → 回退到能处理该节点的默认渲染器 → 仍失败 → 源码纯文本块 |
| `IDiagramRenderer` 抛异常/超时 | 回退代码块 + warning |
| `ICodeHighlighter` 异常 | 回退纯文本 + warning |
| 图片加载失败 | 占位 + warning（不阻塞渲染） |
| 流式不完整 | 不是错误：占位/半成品，完成后校正 |
| `Warning` 订阅者抛异常 | 吞掉并记录（绝不让宿主回调炸 UI） |

---

## 13. 性能与虚拟化

| 指标 | 目标（M0 校准） |
| --- | --- |
| 首屏渲染 20KB Markdown | P95 ≤ 100ms |
| 追加 100 字符（后台解析 + UI 嫁接） | P95 ≤ 16ms |
| 尾部窗口重解析 | ≤ 1ms（窗口 8–16KB） |
| 1MB 文档首屏（后台解析） | ≤ 400ms，UI 不阻塞超过 2 帧 |
| 1000 块消息滚动 | 默认 `StackPanel` 下 60fps 目标；不足时启用虚拟化 |
| 内存（单条 1MB 消息） | ≤ 3× 文本大小 |

- 解析合帧（16ms）；块控件复用（`IIncrementalPuraRenderer`）；图片异步；高亮只在块稳定后执行。
- 虚拟化：`Options.EnableBlockVirtualization`（M3 验证 `ItemsRepeater`/`VirtualizingStackPanel` 的变高表现）；默认关闭，保证正确性。
- 长会话由宿主做**消息级虚拟化**（只创建可见消息的 `PuraMarkdownView`）。

---

## 14. 测试策略

| 类型 | 方式 |
| --- | --- |
| 解析 | `Markdig` AST 断言（节点类型/Info/IsClosed/Span） |
| 渲染 | `Avalonia.Headless.XUnit`：`PuraMarkdownView` 离屏渲染 → `ControlTreeDumper.Dump()` 生成确定性文本 → golden 快照 |
| 流式 | 属性测试：随机切分点逐段 `Append` + `Complete` 后的 dump，必须等于一次性渲染的 dump（交互禁用） |
| 交互 | Headless 输入模拟点击选项 → 断言 `InteractionOccurred` 参数；只读模式断言无事件 |
| 降级 | 非法 JSON/未知组件/插件抛异常/无插件图表 → 断言降级卡与 warning |
| 安全 | HTML/URL/超长/深嵌套语料 → 断言转义、拒绝、无超时 |
| 性能 | BenchmarkDotNet：解析、追加、1MB 首屏；CI 阈值回归 |

> 无 IR 后，"确定性基线"由**控件树 dump** 承担：dump 必须只包含结构、类型、关键属性与文本，不含运行时随机值（时间、GUID）。

---

## 15. 包结构与依赖

| 包 | 内容 | 依赖（版本基线见 §16） |
| --- | --- | --- |
| `PuraMarkdown` | 控件、契约、解析路由、分发、默认渲染、`pura-agent`、流式 | Avalonia、Markdig、DI/Options/Logging、JsonSchema.Net |
| `PuraMarkdown.Testing` | 无头测试宿主、`ControlTreeDumper`、golden 工具、生成器 | xunit.v3、Avalonia.Headless.XUnit |
| `PuraMarkdown.Highlighting.ColorCode`（可选） | `ICodeHighlighter` 实现 | ColorCode.Core 2.0.15 |
| `PuraMarkdown.Diagrams.Mermaid`（可选，M2） | `IDiagramRenderer` 实现 | Mermaider / Naiad（spike 定） |
| `PuraMarkdown.Diagrams.ScottPlot`（可选，M3） | `IDiagramRenderer`（`pura-chart` JSON） | ScottPlot.Avalonia 5.1.59 |
| `PuraMarkdown.Math.CSharpMath`（可选，1.1） | 公式渲染 | CSharpMath.Avalonia 0.5.1 |
| `samples/PuraMarkdown.Demo` | 演示应用（流式 + 交互 + 主题） | CommunityToolkit.Mvvm 8.4.2 |
| `samples/spike` | M0 技术验证 | — |

> 核心包只依赖 Avalonia + Markdig + BCL/微软扩展；图表、高亮、公式引擎都在可选包，不进核心。

## 16. 版本基线

| 项 | 版本 |
| --- | --- |
| SDK / TFM | 10.0.114 / **net10.0** |
| Avalonia（含 Desktop/Fluent/Headless.XUnit） | **12.1.5** |
| Markdig | **1.5.1** |
| Microsoft.Extensions.*（DI/Options/Logging） | **10.0.14** |
| JsonSchema.Net | **9.6.0** |
| Avalonia.AvaloniaEdit / .TextMate | 12.0.0（可选） |
| TextMateSharp / .Grammars | 2.0.5（可选） |
| ColorCode.Core | 2.0.15（可选） |
| ScottPlot.Avalonia / CSharpMath.Avalonia | 5.1.59 / 0.5.1（可选） |
| Mermaider / Naiad | 0.15.1 / 2.0.1（可选，spike） |
| Avalonia.Controls.ItemsRepeater | 12.0.0（可选虚拟化） |
| xunit.v3 / Microsoft.NET.Test.Sdk / BenchmarkDotNet | 4.0.2 / 18.11.0 / 0.15.8 |
| CommunityToolkit.Mvvm | 8.4.2（示例） |

---

## 17. 关键设计决策索引

| ADR | 决策 | 状态 |
| --- | --- | --- |
| ADR-0009 | 1.0 不渲染 HTML、不提供子集 | 有效 |
| ADR-0010 | 流式只接受 append，不提供编辑/重写 | 有效 |
| ADR-0006 | 交互只产事件，传输归宿主 | 有效 |
| ADR-0011 | 渲染直接消费 Markdig AST、产出 Avalonia 控件（无 IR） | 新，取代 0001/0003 |
| ADR-0012 | `pura-agent` 围栏 + JSON 为扩展语法主路径 | 新，取代 0002 |
| ADR-0013 | 目标框架 net10.0 | 新，取代 0005 |
| ADR-0014 | 图表/高亮/公式外置为插件契约 | 新，取代 0007 |
| ADR-0015 | 块级脏检查 + 尾部窗口 + 视觉树嫁接 | 新，取代 0004 |
| ADR-0016 | 1.0 只做 Avalonia 原生控件，多平台后置 | 新，取代 0008 |

---

## 18. 风险

| # | 风险 | 缓解 |
| --- | --- | --- |
| R1 | Markdig AST 跨版本不稳定 | 锁 1.5.1；只消费公开节点；升级回归 |
| R2 | 无 IR → 安全过滤分散 | 默认渲染器强制 URL/HTML 策略；安全语料回归 |
| R3 | 流式边界误判 | 尾部窗口 + 等价性属性测试；保守提交 |
| R4 | 插件在 UI 线程抛异常 | 回退链 + warning；渲染隔离 |
| R5 | 交互状态在重渲染中丢失 | 流式完成前禁用；完成后不替换块 |
| R6 | 大文档/长会话性能 | 合帧、窗口解析、消息级虚拟化、块虚拟化（M3） |
| R7 | Avalonia 12 API 演进 | 锁定版本；控件工厂集中 |
| R8 | 插件体积/许可 | 插件独立包；许可核验（MIT/Apache/BSD） |

---

## 19. 架构取舍：为什么 1.0 不引入 IR / 虚拟渲染树 / 适配层

> 完整讨论见 [`07-IR-虚拟渲染树-多平台-讨论与决策.md`](07-IR-虚拟渲染树-多平台-讨论与决策.md)；决策记录 ADR-0020/0021/0022（均已接受）。

| 部分 | 1.0 | 替代方案 | 触发条件 | 回补成本 |
| --- | --- | --- | --- | --- |
| IR | 不实现 | Markdig AST + `IAstSanitizer` 净化 pass | 非 Avalonia 后端 / 服务端渲染 / 节点级缓存 / Markdig 不可适配 breaking | 3–4 周 |
| 虚拟渲染树 | 不实现 | `BlockRecord { Span, Hash, Node, View }` + 块级嫁接 | 多后端 / 块内超长内容 / 渲染结果序列化 | 2–3 周 |
| 渲染适配层 | 不实现 | Avalonia 跨平台 + 薄平台服务接口（§10.1） | Blazor/MAUI/非 Avalonia 宿主 / Avalonia 平台限制 | 3–4 周 |

**三个预留钩子（M0 实现，约 2–3 人日）**：

1. `IAstSanitizer`：一次 AST 遍历标记 HTML/URL/超限并产出诊断，默认渲染器与插件渲染器共享同一结论 → 补足"无 IR 导致的安全过滤分散"。
2. `BlockRecord`：记录块源码 span、hash、AST 节点、控件 → 流式嫁接已有需要；未来可扩成 IR 节点或虚拟树节点。
3. `IPuraPlatformServices`（剪贴板/字体/播报）：薄接口 + 默认实现 → 未来多平台只需换实现，不改控件与插件契约。

> 一句话：**先买"正确与简单"，把"可移植"的期权用钩子保留下来**；期权的行权价（3–4 周/项）写进 ADR，等触发条件出现再付。
