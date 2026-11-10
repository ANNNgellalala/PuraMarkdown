# 公共 API 参考（0.1.0-alpha.1）

> 以当前实现为准。**包与命名空间一一对应**（当前共 3 个 NuGet 包，2026-11-10 改名，见 [ADR-0028](adr/0028-package-identity-rename.md)）：
>
> | 包 | 命名空间 |
> | --- | --- |
> | `PuraMarkdown.Core` | `PuraMarkdown.Core`、`PuraMarkdown.Core.Security` |
> | `PuraMarkdown.Agent` | `PuraMarkdown.Agent`、`PuraMarkdown.Agent.Rendering`、`PuraMarkdown.Agent.Chat`、`PuraMarkdown.Agent.Testing`、`PuraMarkdown.Agent.Localization`、`PuraMarkdown.Agent.Platform` |
> | `PuraMarkdown.Extension`（可选） | `PuraMarkdown.Extension.Highlighting.ColorCode`、`PuraMarkdown.Extension.Diagrams.Mermaid` |
>
> 下文小节标题用括号标注所属包；旧包名与旧命名空间（`PuraMarkdown.Syntax`、`PuraMarkdown.Plugins`、`PuraMarkdown`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing`、`PuraMarkdown.Highlighting.ColorCode`、`PuraMarkdown.Diagrams.Mermaid`）**不再保留**，宿主升级时 `PackageReference` 与 `using` 需一起改。

---

## 1. 宿主 API（`PuraMarkdown.Agent` 包）

### 1.1 `PuraMarkdown.Agent.PuraMarkdownView`（`PuraMarkdownView : ContentControl, IDisposable`）

| 成员 | 说明 |
| --- | --- |
| `string? Markdown` | 全量 Markdown；变更触发重渲染 |
| `PuraMarkdownOptions? Options` | 配置 |
| `PuraStreamingSession? ActiveSession` | 当前流式会话 |
| `bool IsStreaming` | 是否流式中 |
| `IReadOnlyList<BlockRecord> Blocks` | 块元数据（高级诊断；虚拟化时 `View` 可能为 null） |
| `PuraStreamingSession BeginStreaming(PuraStreamingOptions? options = null)` | 开始流式 |
| `void Render()` | 全量重渲染 |
| `void AbortStreaming()` | 中断流式并保留内容 |
| `void Announce(string message)` | 触发无障碍播报 |
| `void Prewarm(string markdown)` | 预热首次渲染路径 |
| `void ScrollToEnd()` | 块级虚拟化模式滚动到底部 |
| `IReadOnlyList<PuraHeadingInfo> GetOutline()` | 标题大纲（Level/Text/Anchor） |
| `InteractionStates` | 已上报交互状态（ComponentId 索引） |
| `SetInteractionState` / `ApplyInteractionStates` / `ClearInteractionStates` | 状态持久化/恢复（恢复时自动重渲染） |
| `string GetPlainText()` | 纯文本提取 |
| `Task CopyAllAsync(CancellationToken = default)` | 复制纯文本 |
| `bool ScrollToAnchor(string anchor)` | 滚动到标题锚点 |
| `void RefreshTheme()` | 按当前主题重渲染 |
| `void Dispose()` | 释放活动会话 |

事件：

| 事件 | 参数 |
| --- | --- |
| `InteractionOccurred` | `PuraInteractionEventArgs`（ComponentType/Id/SelectedIds/Confirmed） |
| `Warning` | `PuraWarningEventArgs`（Code/Message/NodeType/Renderer） |
| `Announced` | `string` |
| `LinkClicked` | `PuraLinkClickedEventArgs`（Url/Text/Handled） |
| `ImageLoading` / `ImageLoaded` | `PuraImageEventArgs` / `PuraImageLoadedEventArgs` |

### 1.2 `PuraMarkdownOptions`

```csharp
new PuraMarkdownOptions
{
    Pipeline = pipeline,              // 必填
    Theme = PuraTheme.Light,
    FollowSystemTheme = false,
    DarkTheme = null,
    InteractionEnabled = true,
    UrlPolicy = DefaultUrlPolicy.Instance,
    ImageLoader = NullImageLoader.Instance,
    LinkHandler = NullLinkHandler.Instance,
    Clipboard = NullClipboard.Instance,
    Limits = new PuraLimits(),
    Localizer = ChinesePuraLocalizer.Instance,
    EnableTextSelection = false,
    EnableBlockVirtualization = false,
};
```

> `PuraMarkdownOptions`/`PuraTheme`/`ChinesePuraLocalizer` 在 `PuraMarkdown.Agent` 包；`PuraLimits`、`DefaultUrlPolicy`、`IAstSanitizer` 在 `PuraMarkdown.Core` 包（命名空间 `PuraMarkdown.Core.Security`），需要时补 `using PuraMarkdown.Core.Security;`。

### 1.3 `PuraPipelineBuilder`

| 方法 | 说明 |
| --- | --- |
| `UseCommonMark()` / `UseGfm()` | CommonMark / GFM |
| `UseFootnotes()` / `UseDefinitionLists()` / `UseAlertBlocks()` | 脚注 / 定义列表 / GitHub Alerts |
| `UseEmoji(bool enableSmileys = false)` | Emoji 短代码；smiley 需显式开启 |
| `UseMathematics()` | 数学公式语法（`$...$` / `$$...$$`）；核心默认 TeX 文本回退，排版由插件接管 |
| `UseRenderer(IPuraNodeRenderer, int priority = 100)` | 块/节点渲染器 |
| `UseInlineRenderer(IPuraInlineRenderer, int priority = 100)` | 行内渲染器 |
| `UseFenceRenderer(IFenceRenderer, int priority = 100)` | 围栏渲染器 |
| `UseCodeHighlighter(ICodeHighlighter)` | 代码高亮 |
| `UseSanitizer(IAstSanitizer)` | AST 清洗 |
| `UseMarkdownExtension(IMarkdownExtension)` | Markdig 扩展（高级） |
| `UsePlugin(IPuraPlugin)` | 插件 |
| `PuraPipeline Build()` | 冻结环境 |

`PuraPipeline.Parse(string) → MarkdownDocument`；`Environment` 暴露 `Renderers/Sanitizer/Highlighter/PluginNames`。

> 数学公式：`UseMathematics()` 只启用语法与 TeX 文本回退。需要排版时用 `UseInlineRenderer` 接管 `MathInline`、用 `UseRenderer`（priority > 5）接管 `MathBlock`；参考 `samples/PuraMarkdown.Plugins.Samples/CSharpMathMathPlugin.cs`（CSharpMath 仅示例项目引用，`PuraMarkdown.Core`/`PuraMarkdown.Agent` 均不依赖）。

### 1.4 流式

```csharp
public sealed class PuraStreamingOptions
{
    public bool Synchronous { get; init; }          // 测试/控制台专用
    public int TailWindowChars { get; init; } = 16 * 1024;
    public int MaxTailChars { get; init; } = 1024 * 1024;
    public bool AutoScrollToEnd { get; init; } = true;
}

public sealed class PuraStreamingSession : IAsyncDisposable
{
    PuraStreamingState State { get; }
    string CurrentText { get; }
    int PendingChars { get; }
    PuraStreamingStatistics Statistics { get; }
    Exception? LastWorkerError { get; }

    event EventHandler? Completed;
    event EventHandler? Aborted;

    void Append(string delta);
    Task AppendAsync(ReadOnlyMemory<char> delta, CancellationToken cancellationToken = default);
    Task FlushAsync();
    Task WaitForIdleAsync();
    Task CompleteAsync(CancellationToken cancellationToken = default);
    Task AbortAsync(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}
```

`PuraStreamingState`：`Created/Streaming/Completed/Aborted`。
`PuraStreamingStatistics`：Flush/Commit 次数、已提交/尾部字符、解析量等。

### 1.5 交互状态

```csharp
IReadOnlyDictionary<string, PuraInteractionEventArgs> InteractionStates { get; }
void SetInteractionState(PuraInteractionEventArgs state);
void ApplyInteractionStates(IEnumerable<PuraInteractionEventArgs> states);
void ClearInteractionStates();

string json = PuraInteractionStates.Serialize(states);
var restored = PuraInteractionStates.Deserialize(json);
```

### 1.6 主题/本地化/预算

- `PuraTheme`：`Light`/`Dark` + `Colors/Typography/Spacing/CodeTheme`。
- `PuraLocalizer`：`IPuraLocalizer.Get(key, args)`；内置 `ChinesePuraLocalizer`；键见 `PuraStringKeys`。
- `PuraLimits`：`MaxDocumentChars/MaxFenceChars/MaxBlocks/MaxNestingDepth`。

---

## 2. 语法层（`PuraMarkdown.Core` 包，无 Avalonia）

```csharp
namespace PuraMarkdown.Core;

public sealed class PuraSyntaxBuilder
{
    PuraSyntaxBuilder UseCommonMark();
    PuraSyntaxBuilder UseGfm();
    PuraSyntaxBuilder UseFootnotes();
    PuraSyntaxBuilder UseDefinitionLists();
    PuraSyntaxBuilder UseAlertBlocks();
    PuraSyntaxBuilder UseEmoji(bool enableSmileys = false);
    PuraSyntaxBuilder UseMathematics();
    PuraSyntaxBuilder UseMarkdownExtension(IMarkdownExtension extension);
    PuraSyntaxBuilder UseSanitizer(IAstSanitizer sanitizer);
    PuraSyntaxBuilder ConfigureLimits(Action<PuraLimits> configure);
    PuraSyntaxBuilder UseUrlPolicy(IUrlPolicy urlPolicy);
    PuraSyntaxPipeline Build();
}

public sealed class PuraSyntaxPipeline
{
    MarkdownPipeline MarkdownPipeline { get; }
    PuraSyntaxOptions Options { get; }
    bool MathEnabled { get; }
    PuraSyntaxOptions CreateOptions(PuraLimits limits, IUrlPolicy urlPolicy);

    MarkdownDocument Parse(string markdown);
    PuraDocument ParseDocument(string markdown, int version = 0, PuraSyntaxOptions? options = null);
    PuraDocument ParseIncremental(PuraDocument previous, PuraTextChange change);
}

public sealed class PuraSyntaxOptions
{
    PuraLimits Limits { get; init; }
    IUrlPolicy UrlPolicy { get; init; }
    IAstSanitizer Sanitizer { get; init; }
}

public sealed record PuraDocument(
    int Version, string Text, MarkdownDocument Markdown,
    IReadOnlyList<PuraDiagnostic> Diagnostics, PuraOutline Outline,
    SanitizeResult? Sanitized = null);

public sealed class PuraSyntaxStreamingSession
{
    PuraDocument? Current { get; }
    PuraDocument? Committed { get; }
    PuraDocument? Tail { get; }
    int CommittedOffset { get; }
    void Append(string delta);
    PuraDocument Complete();
}
```

- `PuraDocument.Diagnostics` 由 sanitizer/limits 生成（`PURA-SEC-*`/`PURA-LIM-*`）；
- `PuraDocument.Sanitized` 包含阻断节点集合，渲染层直接复用，避免二次清洗；
- `PuraDocumentView` 接受已解析文档直接渲染（该类型在 `PuraMarkdown.Agent` 包，命名空间 `PuraMarkdown.Agent`，见 §2.2）；
- `IPuraNodeRenderer` 输入 `MarkdownObject`，Arc 可实现自定义渲染器；
- `ParseIncremental` 按顶层块定位受影响区间（前后扩一块）只重解析该段，复用其余 AST；引用定义/异常回退全量；命中时 `previous` 被消费。

### 2.1 语法节点兼容矩阵

```csharp
public static class MarkdigCompatibility
{
    public const string TestedPackageVersion = "1.5.1";
    public const string TestedAssemblyVersion = "1.5.0.0";

    public static string CurrentAssemblyVersion { get; }
    public static IReadOnlyList<PuraNodeSupportInfo> Nodes { get; }
    public static IReadOnlyList<Type> SupportedNodeTypes { get; }

    public static bool IsSupported(MarkdownObject node);
    public static PuraNodeSupportInfo? Describe(MarkdownObject node);
}
```

`PuraNodeSupport`：`Rendered / Structural / ParsedNotRendered / Fallback / NotContract`。  
完整节点表与升级检查清单见 [Markdig 支持节点子集与兼容矩阵](35-Markdig支持节点子集与兼容矩阵.md)。

### 2.2 文档渲染宿主（`PuraMarkdown.Agent` 包，命名空间 `PuraMarkdown.Agent`）

```csharp
public sealed class PuraDocumentView : ContentControl
{
    PuraMarkdownOptions? Options { get; set; }
    PuraDocument? Document { get; set; }

    event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;
    event EventHandler<PuraWarningEventArgs>? Warning;
    event EventHandler<PuraLinkClickedEventArgs>? LinkClicked;
    event EventHandler<PuraImageEventArgs>? ImageLoading;
    event EventHandler<PuraImageLoadedEventArgs>? ImageLoaded;
}
```

`PuraMarkdownView.RenderDocument(PuraDocument)` 也可直接渲染预解析文档，不重新解析。

---

## 3. 插件契约（主要在 `PuraMarkdown.Agent` 包；`IAstSanitizer`/`SanitizeResult` 在 `PuraMarkdown.Core` 包）

```csharp
public interface IPuraPlugin
{
    string Name { get; }
    string Version { get; }
    string? Description { get; }
    void Configure(PuraPipelineBuilder builder);
}

public interface IPuraNodeRenderer
{
    bool CanRender(MarkdownObject node, RenderContext context);
    Control Render(MarkdownObject node, RenderContext context);
}

// 兼容旧名；等价于 IPuraNodeRenderer
public interface IPuraRenderer : IPuraNodeRenderer
{
}

public interface IIncrementalPuraRenderer : IPuraRenderer
{
    bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context);
    void Update(Control existing, MarkdownObject newNode, RenderContext context);
}

public interface IPuraInlineRenderer : IPuraRenderer
{
    Inline RenderInline(MarkdownObject node, RenderContext context);
}

public interface IFenceRenderer
{
    string Name { get; }
    bool CanRender(string language, string source, bool isClosed, RenderContext context);
    Control Render(string language, string source, bool isClosed, RenderContext context);
}

public interface ICodeHighlighter
{
    bool CanHighlight(string? language);
    IReadOnlyList<HighlightSpan> Highlight(string code, string? language);
}

public interface IAstSanitizer
{
    SanitizeResult Sanitize(MarkdownDocument document, PuraMarkdownOptions options);
}

public sealed class SanitizeResult
{
    IReadOnlyList<string> Warnings { get; }
    bool IsBlocked(MarkdownObject node);
    void Block(MarkdownObject node);
    void AddWarning(string warning);
}
```

`RenderContext`（只读）：`Theme/Options/Renderers/Highlighter/Sanitized/Dispatcher/IsStreaming/IsTail/PrecomputedHighlights/InteractionSink/WarningSink/LinkClickSink/Image*`。

> 包归属：`IPuraPlugin`/`PuraPipelineBuilder`/`RenderContext` 及渲染器契约在 `PuraMarkdown.Agent`（命名空间 `PuraMarkdown.Agent` / `PuraMarkdown.Agent.Rendering`）；`IAstSanitizer`/`SanitizeResult`/`PuraLimits`/`IUrlPolicy` 在 `PuraMarkdown.Core`（命名空间 `PuraMarkdown.Core.Security`），使用后两者时需 `using PuraMarkdown.Core.Security;`。

---

## 4. Agent 组件（命名空间 `PuraMarkdown.Agent`，随 `PuraMarkdown.Agent` 包发布）

```csharp
using PuraMarkdown.Agent;

builder.UseAgentComponents();                       // 扩展方法
builder.UseAgentComponent(parser, renderer);        // 追加自定义组件
builder.UsePlugin(new AgentPlugin());               // 显式

public sealed class AgentPlugin : IPuraPlugin
{
    IReadOnlyList<string> ComponentTypes { get; }
    AgentPlugin();
    AgentPlugin(IEnumerable<IAgentComponentParser>? parsers,
                IEnumerable<IAgentComponentRenderer>? renderers);
}

public interface IAgentComponentParser
{
    string ComponentType { get; }
    JsonSchema PayloadSchema { get; }
    bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error);
}

public interface IAgentComponentRenderer
{
    string ComponentType { get; }
    Control RenderComponent(PuraAgentComponent component, RenderContext context);
}
```

模型：`PuraAgentComponent`、`PuraChoiceComponent/Option`、`PuraConfirmComponent`、`PuraComponentError`。
工具调用（tool call）：`pura-agent` 组件支持 `"type":"tool-call"` 类型，另有独立的 `pura-tool`（别名 `pura-tool-call`）围栏，均由 `PuraMarkdown.Agent` 包提供。
未启用 Agent 扩展（未调用 `UseAgentComponents()` / `UsePlugin(new AgentPlugin())`）时：`pura-agent` = 普通代码块。

---

## 5. Chat 控件（命名空间 `PuraMarkdown.Agent.Chat`，随 `PuraMarkdown.Agent` 包发布）

```csharp
public enum PuraChatRole { User, Assistant, System, Tool }
public enum PuraChatMessageStatus { Complete, Streaming, Failed, Aborted }
public enum PuraChatAutoScrollMode { Never, NearBottom, Always }

public sealed class PuraChatMessage
{
    string Id { get; init; }
    PuraChatRole Role { get; init; }
    string Markdown { get; set; }          // 变更递增 ContentVersion/UpdatedAt
    PuraChatMessageStatus Status { get; set; }
    string? Error { get; set; }
    DateTimeOffset CreatedAt { get; init; }
    DateTimeOffset UpdatedAt { get; }
    int ContentVersion { get; }
    IDictionary<string, string> Metadata { get; }
    IReadOnlyList<PuraToolCall> ToolCalls { get; set; }  // 工具调用（tool call）列表
    string? ToolCallId { get; set; }                     // 工具结果消息（Role=Tool）配对的调用 Id
}

// 工具调用模型（`PuraMarkdown.Agent` 命名空间，随 `PuraMarkdown.Agent` 包发布）
public enum PuraToolCallStatus { Running, Succeeded, Failed, Cancelled }
public sealed record PuraToolCall
{
    string Id { get; init; }
    string Name { get; init; }
    PuraToolCallStatus Status { get; init; }
    string? Arguments { get; init; }
    string? Result { get; init; }
    string? Error { get; init; }
    TimeSpan? Duration { get; init; }

    static bool TryParse(string json, out PuraToolCall? call, out string? error);
}

public sealed class PuraChatView : ContentControl
{
    PuraMarkdownOptions? Options { get; set; }
    PuraChatAutoScrollMode AutoScrollMode { get; set; }
    ObservableCollection<PuraChatMessage> Messages { get; }
    PuraStreamingSession? ActiveSession { get; }
    bool IsStreaming { get; }
    bool IsNearBottom { get; }

    event EventHandler<PuraChatMessage>? MessageAdded;
    event EventHandler<PuraChatMessage>? MessageUpdated;
    event EventHandler<PuraChatMessage>? MessageRemoved;
    event EventHandler<PuraInteractionEventArgs>? InteractionOccurred;
    event EventHandler<PuraWarningEventArgs>? Warning;

    void AddMessage(PuraChatMessage message);
    void InsertMessage(int index, PuraChatMessage message);
    void UpdateMessage(PuraChatMessage message);
    bool RemoveMessage(string messageId);
    void ClearMessages();
    void ReplaceMessages(IEnumerable<PuraChatMessage> messages);
    PuraChatMessage? FindMessage(string messageId);

    PuraStreamingSession BeginStreamingMessage(PuraChatMessage? message = null);
    void AbortStreaming();
    void ScrollToEnd();
    void ScrollToMessage(string messageId);

    PuraChatSnapshot CreateSnapshot();
    void RestoreSnapshot(PuraChatSnapshot snapshot);
}

public static class PuraChatSnapshotSerializer
{
    string Serialize(PuraChatSnapshot snapshot);
    PuraChatSnapshot Deserialize(string json);
}
```

- `PuraChatRole.Tool` 用于工具结果消息；消息上另有工具调用（tool calls）字段，与 `pura-tool` 围栏配合展示工具调用与结果。

---

## 6. 平台契约（`PuraMarkdown.Agent.Platform`）

```csharp
public interface IImageLoader
{
    Task<IImageResult> LoadAsync(string url, CancellationToken cancellationToken = default);
}

public interface ILinkHandler
{
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}

public interface IPuraClipboard
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
```

`IImageResult`：`Success(Bitmap)` / `Refused(string)` / `Failed(string)`。
默认实现均为离线安全实现（拒绝图片/不打开链接/无剪贴板）。

---

## 7. 测试工具（命名空间 `PuraMarkdown.Agent.Testing`，随 `PuraMarkdown.Agent` 包发布）

- `ControlTreeDumper.Dump/Find<T>`；
- `GoldenFile.AssertMatches`；
- `InteractionProbe`（查找/点击）；
- `StreamingDriver`；
- `PluginConformance`。

---

## 8. 官方扩展包（`PuraMarkdown.Extension`，可选）

该包合并了原 `PuraMarkdown.Highlighting.ColorCode` 与 `PuraMarkdown.Diagrams.Mermaid`，命名空间随包迁到 `PuraMarkdown.Extension.*`：

```csharp
using PuraMarkdown.Extension.Highlighting.ColorCode;   // ColorCodeHighlighter、ColorCodePlugin
using PuraMarkdown.Extension.Diagrams.Mermaid;          // MermaidDiagramRenderer、MermaidPlugin
```

- `ColorCodeHighlighter : ICodeHighlighter`、`ColorCodePlugin : IPuraPlugin`（命名空间 `PuraMarkdown.Extension.Highlighting.ColorCode`）；
- `MermaidDiagramRenderer : IFenceRenderer`、`MermaidPlugin : IPuraPlugin`（命名空间 `PuraMarkdown.Extension.Diagrams.Mermaid`）。

```csharp
var pipeline = new PuraPipelineBuilder()
    .UseCommonMark()
    .UseGfm()
    .UsePlugin(new ColorCodePlugin())   // 或 .UseCodeHighlighter(new ColorCodeHighlighter())
    .UsePlugin(new MermaidPlugin())     // 或 .UseFenceRenderer(new MermaidDiagramRenderer())
    .Build();
```

引擎依赖（ColorCode.Core 2.0.15、Mermaider 0.15.1）只在此包；未引用 `PuraMarkdown.Extension` 时，代码高亮回退纯等宽文本、Mermaid 围栏回退代码块。决策见 [ADR-0014](adr/0014-plugins-for-diagrams-and-highlighting.md) / [ADR-0027](adr/0027-package-consolidation.md) / [ADR-0028](adr/0028-package-identity-rename.md)。

---

## 9. 稳定性说明

| 级别 | 内容 |
| --- | --- |
| 稳定（宿主） | View/Options/PipelineBuilder/Streaming/Events/Theme/Localizer/Limits |
| 稳定（插件） | IPuraPlugin/Renderer/Inline/Fence/Highlighter/Sanitizer/Agent 组件契约 |
| 高级 | RenderContext、PuraRendererRegistry、BlockRecord、Markdig 扩展 |
| 内部 | 默认渲染器、流式扫描器、Agent Block 解析等 |

> `0.1.0-alpha.1` 仍允许破坏性调整；1.0 前会加入 PublicAPI baseline 与分析器。包整合（7 → 3，ADR-0027）不改公共类型签名，只改 `PackageReference`；包改名（ADR-0028）同时改包 id 与命名空间，属 BREAKING。










