> 历史文档：记录当时的设计/实现，API 可能已过时。当前接入请阅读 [应用集成指南](26-应用集成指南.md) / [迁移指南](27-迁移指南-0.1.0-alpha.1.md) / [公共 API 参考](28-公共API参考.md)。

# 插件与渲染契约（v0.3）

> 状态：草案 v0.3（按提案重构：Markdig AST 直连 Avalonia，无 IR）
> 关联：[架构设计](./02-architecture.md) · [Agent 扩展语法规范](./03-agent-syntax-spec.md) · [需求对齐](./00-需求对齐与设计复审.md)
> 目标读者：插件作者、宿主开发者、PuraMarkdown 贡献者。

---

## 1. 设计目标

1. **契约窄**：只保留五类扩展点（解析、渲染、Agent 组件、图表、高亮），不引入编辑/模板/布局契约。
2. **基础可覆盖**：默认渲染器是注册表中的低优先级项，插件用更高优先级覆盖，而非"特判分支"。
3. **组合灵活**：同一节点可被不同插件以不同优先级争夺；规则明确、可预测。
4. **失败隔离**：插件异常不得拖垮整条消息；回退链 + 非致命 `Warning`。
5. **AOT/裁剪友好**：显式注册，不做程序集扫描/反射发现。

---

## 2. 扩展点矩阵

| 扩展点 | 契约 | 注册入口 | 典型实现 |
| --- | --- | --- | --- |
| 语法（新 Markdig 扩展） | `IMarkdownExtension` + 自定义 `BlockParser`/`InlineParser` | `UseMarkdownExtension(...)` | 容器指令插件、行内语法插件 |
| 节点渲染 | `IPuraRenderer`（+ `IIncrementalPuraRenderer`） | `UseRenderer(renderer, priority)` | 自定义代码块、表格、callout |
| 行内渲染 | `IPuraInlineRenderer` | `UseInlineRenderer(renderer, priority)` | 行内徽标、图标、数学符号 |
| Agent 组件解析 | `IAgentComponentParser` | `UseAgentComponentParser(parser)` | `choice`/`confirm`/自定义 type |
| Agent 组件渲染 | `IAgentComponentRenderer` | `UseAgentComponentRenderer(renderer, priority)` | 交互列表、评分、表单 |
| 图表 | `IDiagramRenderer` | `UseDiagramRenderer(renderer)` | Mermaid、ScottPlot、PlantUML |
| 代码高亮 | `ICodeHighlighter` | `UseCodeHighlighter(highlighter)` | ColorCode、TextMateSharp |
| 主题 | `PuraTheme`（数据） | `PuraMarkdownOptions.Theme` | 品牌主题包 |
| 图片/链接/URL | `IImageLoader` / `ILinkHandler` / `IUrlPolicy` | `PuraMarkdownOptions` / builder | 宿主实现 |
| 打包单元 | `IPuraPlugin` | `UsePlugin(plugin)` | 分发聚合 |

> 没有"模板/样式表/布局"扩展点：只读场景下，控件即结果；样式通过主题 token 表达。

---

## 3. 核心契约

### 3.1 渲染器

```csharp
namespace PuraMarkdown.Rendering;

public interface IPuraRenderer
{
    /// 廉价、确定性、无副作用。禁止在此发起 I/O、创建控件或修改全局状态。
    bool CanRender(MarkdownObject node, RenderContext context);

    /// 必须在 UI 线程调用；返回 Avalonia Control。
    /// 容器类渲染器可用 context.RenderChildren(...) 递归渲染子节点。
    Control Render(MarkdownObject node, RenderContext context);
}

public interface IIncrementalPuraRenderer : IPuraRenderer
{
    /// 节点类型不变、内容变化（流式尾部块、交互状态）时，是否可以就地更新
    bool CanUpdate(MarkdownObject oldNode, MarkdownObject newNode, RenderContext context);

    /// 就地更新已有控件（不得替换控件本身，保证焦点/选择状态不丢）
    void Update(Control existing, MarkdownObject newNode, RenderContext context);
}

public interface IPuraInlineRenderer : IPuraRenderer
{
    /// 行内扩展节点 → Avalonia Inline（自动包 InlineUIContainer 或直接返回 Run）
    Inline RenderInline(MarkdownObject node, RenderContext context);
}
```

### 3.2 渲染上下文

```csharp
public sealed class RenderContext
{
    public required PuraTheme Theme { get; init; }
    public required IImageLoader ImageLoader { get; init; }
    public required ILinkHandler LinkHandler { get; init; }
    public required IUrlPolicy UrlPolicy { get; init; }
    public required PuraLimits Limits { get; init; }
    public required IDispatcher Dispatcher { get; init; }          // Avalonia.Threading
    public required bool IsStreaming { get; init; }
    public required PuraMessageInfo Message { get; init; }          // MessageId / Role / Index
    public PuraComponentFactory Components { get; init; }           // 组件类型 → 解析器/渲染器
    public ILogger? Logger { get; init; }

    // 组合辅助
    public Control RenderMarkdown(MarkdownObject node);
    public IEnumerable<Control> RenderChildren(ContainerBlock container);
    public Inline RenderInlineChildren(ContainerInline container);
}
```

- 上下文是**只读服务包**，不是 service locator：插件需要的能力都在这里显式声明。
- 宿主扩展需求（如"打开外部浏览器"）通过 `ILinkHandler`/`IImageLoader`/自定义服务注入；如需更多服务，插件可定义自己的接口并在宿主侧实现后传入渲染器构造参数（推荐），而不是让核心认识它。

### 3.3 Agent 组件

```csharp
public abstract record PuraAgentComponent
{
    public required string Type { get; init; }
    public required string Version { get; init; }
    public string? Id { get; init; }
}

public interface IAgentComponentParser
{
    string ComponentType { get; }                       // "choice"
    JsonSchema PayloadSchema { get; }
    bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error);
}

public interface IAgentComponentRenderer
{
    /// 组件类型，与 IAgentComponentParser.ComponentType 对应
    string ComponentType { get; }

    /// 解析成功后由核心按 ComponentType 查找并调用
    Control RenderComponent(PuraAgentComponent component, PuraAgentBlock node, RenderContext context);
}
```

- 分发顺序：`PuraAgentBlock` → 找到能渲染它的 `IAgentComponentRenderer`（按组件 type/id）→ 其内部调用 `IAgentComponentParser` 得到模型 → `RenderComponent`。
- 核心内置 `choice`、`confirm` 的解析器与渲染器；插件注册新 type 即扩展。
- 未知 type / 解析失败 → 核心的统一降级卡（不交给插件）。

### 3.4 图表与高亮

```csharp
public interface IDiagramRenderer
{
    bool CanRenderDiagram(string language, string source, RenderContext context);
    Control RenderDiagram(string language, string source, RenderContext context);
}

public interface ICodeHighlighter
{
    bool CanHighlight(string? language);
    /// 返回按 token 切分的片段；坐标基于 code 的 UTF-16 偏移
    IReadOnlyList<HighlightSpan> Highlight(string code, string? language);
}
public readonly record struct HighlightSpan(int Start, int Length, string Token);
```

- 图表分发：`FencedCodeBlock` → 依次询问 `IDiagramRenderer`（按注册顺序）→ 命中则渲染；否则代码块。
- 高亮分发：代码块渲染器询问 `ICodeHighlighter.CanHighlight(language)`；命中则着色；否则纯等宽。
- 两者都不得在 `Render` 内同步联网/阻塞；异步资源通过宿主注入。

### 3.5 插件打包

```csharp
public interface IPuraPlugin
{
    string Name { get; }                       // "acme.mermaid"，建议含命名空间
    string Version { get; }                    // 插件版本（SemVer）
    string? Description { get; }
    void Configure(PuraPipelineBuilder builder);
}
```

`Configure` 只做注册，不做解析/渲染；`Build()` 时校验：

| 校验 | 失败处理 |
| --- | --- |
| 渲染器/解析器为空或 type 非法 | 抛 `PuraConfigurationException`（fail fast） |
| 同一 `IAgentComponentParser.ComponentType` 重复注册 | 后者覆盖 + warning（或抛错，严格模式） |
| 高优先级渲染器 `CanRender` 恒为 true | warning（可能吞掉所有节点） |
| `IDiagramRenderer` 语言重复 | 先注册者优先 + warning |
| 插件名重复 | 抛错 |

---

## 4. 注册与优先级

### 4.1 注册表规则

- 渲染器列表按 **priority 降序**稳定排序；同优先级保持注册顺序。
- 解析：`foreach (renderer in ordered) if (renderer.CanRender(node, ctx)) return renderer;`
- 基础默认渲染器：priority **0**；核心内置的"专门渲染器"（代码块、表格、`PuraAgentBlock`）也是 0，但 `CanRender` 更具体，天然先于"通用兜底"。
- 插件建议 priority **100–500**；基础设施级（如全量自定义代码块）可到 **1000**。
- 覆盖内置的推荐方式：**同节点类型 + 更高优先级**，不要修改核心注册表。

### 4.2 内置渲染器与优先级

| 渲染器 | 目标节点 | priority | 说明 |
| --- | --- | --- | --- |
| `ParagraphRenderer` | `ParagraphBlock` | 0 | 行内组合 → `SelectableTextBlock` |
| `HeadingRenderer` | `HeadingBlock` | 0 | 标题层级样式 |
| `ListRenderer` | `ListBlock` | 0 | 有序/无序/任务列表 |
| `QuoteRenderer` | `QuoteBlock` | 0 | 左边框 + 递归子块 |
| `CodeBlockRenderer` | `FencedCodeBlock`/`CodeBlock` | 0 | 语言标签/复制/高亮注入 |
| `TableRenderer` | `Table` | 0 | `Grid` |
| `ThematicBreakRenderer` | `ThematicBreakBlock` | 0 | 分隔线 |
| `HtmlFallbackRenderer` | `HtmlBlock`/`HtmlInline` | 0 | 转义文本 |
| `DiagramDispatchRenderer` | 已注册语言的 `FencedCodeBlock` | 50 | 先于代码块；内部询问 `IDiagramRenderer` |
| `AgentComponentRenderer` | `PuraAgentBlock` | 100 | 组件解析 + 组件渲染 |
| `FallbackTextRenderer` | 任意 `MarkdownObject` | int.MinValue | 最后兜底：源码文本 |

> `DiagramDispatchRenderer` 只在存在 `IDiagramRenderer` 时注册；无插件时图表围栏走 `CodeBlockRenderer`（源码可复制）。

### 4.3 优先级示例

```csharp
// 让所有代码块改用 TextMate 高亮（覆盖内置 CodeBlockRenderer）
builder.UseRenderer(new TextMateCodeRenderer(), priority: 300);
// 让 mermaid 围栏走 Mermaid 渲染器（DiagramDispatch 会先问 IDiagramRenderer）
builder.UseDiagramRenderer(new MermaidDiagramRenderer());
```

---

## 5. 默认渲染组实现要点

| 节点 | 控件与结构 | 关键细节 |
| --- | --- | --- |
| 段落 | `SelectableTextBlock` | 行内 `Run`；`TextWrapping=Wrap`；`IsTextSelectionEnabled` 可配 |
| 标题 | `TextBlock` | `Level` → 字号/字重；`AutomationProperties.HeadingLevel` |
| 列表 | `Grid`（项目符号列 + 内容列） | 有序用 `OrderedStart`；嵌套递归；任务列表 `CheckBox` 只读 |
| 引用 | `Border`（`BorderThickness` 左 3px）+ 内容 `StackPanel` | 递归渲染子块 |
| 代码块 | `Border` + 头（语言 + 复制）+ `SelectableTextBlock` | 横向滚动；高亮 token → `Run.Foreground`；复制走剪贴板 |
| 表格 | `Grid` + `Border` 单元格 | 表头加粗/背景；列对齐；超宽外层 `ScrollViewer` |
| 分隔线 | `Border`（1px） | 主题 `Colors.Border` |
| HTML | `TextBlock`（转义原文） | 不执行；保留可复制 |
| 链接 | `HyperlinkButton` / 带下划线 `TextBlock` | 先 `IUrlPolicy`，再 `ILinkHandler` |
| 图片 | `Image` / 占位 `Border` | `IImageLoader` 异步；失败显示 alt + 域名 |
| 强调/加粗/删除线 | `Run` 属性 | `FontStyle`/`FontWeight`/`TextDecorations` |
| 行内代码 | `Run` + 背景 | 主题 `CodeTheme.InlineBackground` |

> 所有默认渲染器只通过 `RenderContext.Theme` 取色/取字体，禁止硬编码颜色或字号。

---

## 6. 插件生命周期与分发

### 6.1 注册方式

```csharp
// 推荐：显式实例，AOT 友好、可测试
var pipeline = new PuraPipelineBuilder()
    .UseCommonMark().UseGfm().UseAgentComponents()
    .UsePlugin(new MermaidPlugin())
    .Build();

// DI（可选便利方法，内部仍显式注册）
services.AddPuraMarkdown(b => b.UseCommonMark().UseGfm().UseAgentComponents());
```

- **不做程序集扫描**：显式列表；如需"自动发现"，由宿主自己枚举并调用 `UsePlugin`。
- 插件不得在 `Configure` 中访问 UI/网络；只能注册。
- 插件可依赖 `PuraMarkdown` 与 Avalonia（因为契约类型就是 Avalonia 控件）。

### 6.2 NuGet 约定

| 包 | 内容 | 依赖 |
| --- | --- | --- |
| `PuraMarkdown.Diagrams.Mermaid` | `IPuraPlugin` + `IDiagramRenderer` | Mermaider 或 Naiad（spike 定） |
| `PuraMarkdown.Highlighting.ColorCode` | `ICodeHighlighter` | ColorCode.Core 2.0.15 |
| `PuraMarkdown.Highlighting.TextMate` | `ICodeHighlighter`（AvaloniaEdit/TextMateSharp，重） | Avalonia.AvaloniaEdit 12.0.0、TextMateSharp 2.0.5 |
| `PuraMarkdown.Diagrams.ScottPlot` | `IDiagramRenderer`（`pura-chart` JSON schema 由本包定义） | ScottPlot.Avalonia 5.1.59 |

### 6.3 信任边界

- 插件 = 代码 = 完全信任；不做沙箱，不动态加载不受信程序集。
- 禁止从 LLM 输出中解析"加载插件"指令；插件只能由宿主在启动时注册。
- 插件渲染器若直接执行外部副作用（如 `Process.Start`），责任在插件与宿主；文档明确警示。

---

## 7. 示例插件 A：ColorCode 高亮

```csharp
public sealed class ColorCodeHighlighter : ICodeHighlighter
{
    public bool CanHighlight(string? language)
        => !string.IsNullOrEmpty(language) && ColorCode.Core.Languages.Has(language);

    public IReadOnlyList<HighlightSpan> Highlight(string code, string? language)
    {
        var result = new List<HighlightSpan>();
        foreach (var scope in ColorCode.Core.ColorCode.Highlight(code, language))
            result.Add(new HighlightSpan(scope.Index, scope.Length, scope.ScopeName ?? "text"));
        return result;
    }
}

public sealed class HighlightingPlugin : IPuraPlugin
{
    public string Name => "pura.highlighting.colorcode";
    public string Version => "1.0.0";
    public string? Description => "ColorCode 语法高亮（可选）";
    public void Configure(PuraPipelineBuilder b) => b.UseCodeHighlighter(new ColorCodeHighlighter());
}
```

代码块渲染器：`ICodeHighlighter.Highlight` → 按 span 切分文本 → `Run { Foreground = theme.CodeTheme.Resolve(token) }`；异常/未命中 → 纯文本。

---

## 8. 示例插件 B：Mermaid 图表

```csharp
public sealed class MermaidDiagramRenderer : IDiagramRenderer
{
    public bool CanRenderDiagram(string language, string source, RenderContext ctx)
        => language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public Control RenderDiagram(string language, string source, RenderContext ctx)
    {
        // M2 spike：评估 Mermaider（0.15.1）或 Naiad（2.0.1）的 Avalonia 控件与 TFM
        // 失败时抛异常 → 分发层回退代码块 + warning
        return new MermaidControl(source, ctx.Theme);
    }
}

public sealed class MermaidPlugin : IPuraPlugin
{
    public string Name => "pura.diagrams.mermaid";
    public string Version => "1.0.0";
    public void Configure(PuraPipelineBuilder b) => b.UseDiagramRenderer(new MermaidDiagramRenderer());
}
```

验收标准：**接入真实引擎不需要修改 `IDiagramRenderer` 契约**。若需要改契约，说明边界设计失败，应在 M2 复盘。

---

## 9. 示例插件 C：自定义 Agent 组件 `rating`

```csharp
public sealed record RatingComponent : PuraAgentComponent
{
    public int Value { get; init; }
    public int Max { get; init; } = 5;
    public string? Label { get; init; }
}

public sealed class RatingParser : IAgentComponentParser
{
    public string ComponentType => "rating";
    public JsonSchema PayloadSchema { get; } = RatingSchemas.Schema;   // 自定义 schema
    public bool TryParse(string json, out PuraAgentComponent? c, out PuraComponentError? e)
        => JsonComponentParser.TryParse<RatingComponent>(json, PayloadSchema, out c, out e);
}

public sealed class RatingRenderer : IAgentComponentRenderer
{
    public string ComponentType => "rating";

    // 解析成功后由核心调用；无需关心 PuraAgentBlock 的 JSON 解析
    public Control RenderComponent(PuraAgentComponent c, PuraAgentBlock node, RenderContext ctx)
        => new RatingControl((RatingComponent)c, ctx);
}
```

注册：

```csharp
public sealed class RatingPlugin : IPuraPlugin
{
    public string Name => "acme.rating";
    public string Version => "1.0.0";
    public void Configure(PuraPipelineBuilder b)
    {
        b.UseAgentComponentParser(new RatingParser());
        b.UseAgentComponentRenderer(new RatingRenderer(), priority: 200);
    }
}
```

插件只需实现 `IAgentComponentParser` + `IAgentComponentRenderer`；`pura-agent` 围栏、流式占位、降级卡、事件转发由核心统一处理。

---

## 10. 错误隔离与回退链

```
渲染节点
  → 插件渲染器 Render() 抛异常？
       → 捕获，发 Warning（含插件名、节点类型、异常摘要）
       → 用"跳过该渲染器"的临时上下文重新解析 → 下一个渲染器
       → 直到 FallbackTextRenderer（源码纯文本）
  → 默认渲染器也抛异常？
       → 源码纯文本块（保证消息可读）
```

- 回退只针对"当前节点"，不影响其它块。
- 每个节点最多尝试 N 个渲染器（默认 8），防止插件死循环/重复抛错。
- `Warning` 事件包含：`Code`（如 `PURA-RENDER-001`）、`NodeType`、`RendererName`、`Message`。
- 宿主订阅 `Warning` 做日志/监控；未订阅时静默（不抛出）。

---

## 11. 线程与状态

| 场景 | 规则 |
| --- | --- |
| `Render`/`Update` | 仅 UI 线程；渲染器可创建控件、读取主题 |
| `CanRender` | 任意线程（解析后台线程会调用）；必须无副作用 |
| `IAgentComponentParser.TryParse` | 后台线程（解析阶段）；纯函数、无 UI 访问 |
| 交互事件 | 控件事件 → 库转发到 `InteractionOccurred`（UI 线程） |
| 主题切换 | 重新渲染所有块（控件属性读取主题 token）；插件渲染器需响应 `Update` 或重建 |
| 流式尾部更新 | 优先 `IIncrementalPuraRenderer.Update`；否则替换控件 |

---

## 12. 插件测试

`PuraMarkdown.Testing` 提供：

| 工具 | 用途 |
| --- | --- |
| `PuraTestHost` | 无头 Avalonia 环境（`Avalonia.Headless.XUnit`），创建 `PuraMarkdownView` 并布局 |
| `ControlTreeDumper.Dump(control)` | 确定性控件树文本 dump（结构/类型/文本/关键属性）→ golden 快照 |
| `InteractionProbe` | 模拟点击/键盘，捕获 `InteractionOccurred` |
| `StreamingDriver` | 按任意切分点追加文本，驱动会话到 `Completed` |
| `PuraConformanceSuite` | 插件契约一致性检查（`CanRender` 无副作用、渲染不联网、异常可回退） |

插件 CI 建议至少覆盖：正常渲染 golden、非法输入降级、`CanRender` 边界、线程断言（UI 线程）。

---

## 13. 反模式

| 反模式 | 为什么不做 |
| --- | --- |
| 在 `CanRender` 中创建控件/发起 I/O | 解析可能在后台线程、会被高频调用 |
| 在 `Render` 中同步等待网络 | 阻塞 UI；资源应经 `IImageLoader`/宿主服务 |
| 插件直接改核心注册表/静态状态 | 不可预测、测试困难 |
| 用 `CanRender => true` 的高优先级渲染器"接管一切" | 吞掉所有节点；应只声明自己认识的节点 |
| 在组件 JSON 中放 HTML/Markdown 并二次渲染 | 注入风险；组件文案按纯文本 |
| 依赖 Markdig 内部节点类型 | 升级易碎；只消费公开节点类型 |
| 在插件中管理对话/发送消息 | 越界；库只上报交互事件 |

