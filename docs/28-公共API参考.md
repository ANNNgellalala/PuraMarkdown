# 公共 API 参考（0.1.0-alpha.1）

> 以当前实现为准。命名空间：核心 `PuraMarkdown`、插件契约 `PuraMarkdown.Rendering`、Agent `PuraMarkdown.Agent`、Chat `PuraMarkdown.Chat`、测试 `PuraMarkdown.Testing`。

---

## 1. 宿主 API

### 1.1 `PuraMarkdown.View`（`PuraMarkdownView : ContentControl, IDisposable`）

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

### 1.3 `PuraPipelineBuilder`

| 方法 | 说明 |
| --- | --- |
| `UseCommonMark()` / `UseGfm()` | CommonMark / GFM |
| `UseRenderer(IPuraNodeRenderer, int priority = 100)` | 块/节点渲染器 |
| `UseInlineRenderer(IPuraInlineRenderer, int priority = 100)` | 行内渲染器 |
| `UseFenceRenderer(IFenceRenderer, int priority = 100)` | 围栏渲染器 |
| `UseCodeHighlighter(ICodeHighlighter)` | 代码高亮 |
| `UseSanitizer(IAstSanitizer)` | AST 清洗 |
| `UseMarkdownExtension(IMarkdownExtension)` | Markdig 扩展（高级） |
| `UsePlugin(IPuraPlugin)` | 插件 |
| `PuraPipeline Build()` | 冻结环境 |

`PuraPipeline.Parse(string) → MarkdownDocument`；`Environment` 暴露 `Renderers/Sanitizer/Highlighter/PluginNames`。

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

## 2. 语法层（`PuraMarkdown.Syntax`，无 Avalonia）

```csharp
namespace PuraMarkdown.Syntax;

public sealed class PuraSyntaxBuilder
{
    PuraSyntaxBuilder UseCommonMark();
    PuraSyntaxBuilder UseGfm();
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
- `PuraDocumentView` 接受已解析文档直接渲染；
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

### 2.2 文档渲染宿主

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

## 3. 插件契约

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

---

## 4. Agent 包（`PuraMarkdown.Agent`）

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
未安装 Agent 包：`pura-agent` = 普通代码块。

---

## 5. Chat 包（`PuraMarkdown.Chat`）

```csharp
public enum PuraChatRole { User, Assistant, System }
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

---

## 6. 平台契约（`PuraMarkdown.Platform`）

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

## 7. 测试包（`PuraMarkdown.Testing`）

- `ControlTreeDumper.Dump/Find<T>`；
- `GoldenFile.AssertMatches`；
- `InteractionProbe`（查找/点击）；
- `StreamingDriver`；
- `PluginConformance`。

---

## 8. 稳定性说明

| 级别 | 内容 |
| --- | --- |
| 稳定（宿主） | View/Options/PipelineBuilder/Streaming/Events/Theme/Localizer/Limits |
| 稳定（插件） | IPuraPlugin/Renderer/Inline/Fence/Highlighter/Sanitizer/Agent 组件契约 |
| 高级 | RenderContext、PuraRendererRegistry、BlockRecord、Markdig 扩展 |
| 内部 | 默认渲染器、流式扫描器、Agent Block 解析等 |

> `0.1.0-alpha.1` 仍允许破坏性调整；1.0 前会加入 PublicAPI baseline 与分析器。









