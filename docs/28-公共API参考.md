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
| `UseRenderer(IPuraRenderer, int priority = 100)` | 块渲染器 |
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

## 2. 插件契约

```csharp
public interface IPuraPlugin
{
    string Name { get; }
    string Version { get; }
    string? Description { get; }
    void Configure(PuraPipelineBuilder builder);
}

public interface IPuraRenderer
{
    bool CanRender(MarkdownObject node, RenderContext context);
    Control Render(MarkdownObject node, RenderContext context);
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

## 3. Agent 包（`PuraMarkdown.Agent`）

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

## 4. Chat 包（`PuraMarkdown.Chat`）

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

## 5. 平台契约（`PuraMarkdown.Platform`）

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

## 6. 测试包（`PuraMarkdown.Testing`）

- `ControlTreeDumper.Dump/Find<T>`；
- `GoldenFile.AssertMatches`；
- `InteractionProbe`（查找/点击）；
- `StreamingDriver`；
- `PluginConformance`。

---

## 7. 稳定性说明

| 级别 | 内容 |
| --- | --- |
| 稳定（宿主） | View/Options/PipelineBuilder/Streaming/Events/Theme/Localizer/Limits |
| 稳定（插件） | IPuraPlugin/Renderer/Inline/Fence/Highlighter/Sanitizer/Agent 组件契约 |
| 高级 | RenderContext、PuraRendererRegistry、BlockRecord、Markdig 扩展 |
| 内部 | 默认渲染器、流式扫描器、Agent Block 解析等 |

> `0.1.0-alpha.1` 仍允许破坏性调整；1.0 前会加入 PublicAPI baseline 与分析器。



