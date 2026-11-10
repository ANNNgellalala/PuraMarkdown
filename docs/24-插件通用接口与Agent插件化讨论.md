# 插件通用接口与 Agent 插件化讨论（2026-11-07）

> 状态：方案已确认并实现，见 [Agent 插件化与公共 API 收敛实现](25-Agent插件化与公共API收敛实现.md) 与 [ADR-0024](adr/0024-agent-as-plugin.md)。

> 目标：回答两个问题：
> 1. 插件如何**新增语法块/行内语法/组件**，核心应固定哪些通用接口？
> 2. 把 `pura-agent` 从核心剥离为独立语法插件，是否合理？边界怎么划？
>
> 结论（建议）：**合理，并且适合在 1.0 一起完成**。核心只保留"高性能 Markdown 渲染 + 通用扩展契约"；Agent、Mermaid、Chat 都作为独立包。

---

## 1. 现状与问题

当前核心同时承担：

- Markdown 解析/渲染/流式/虚拟化；
- `pura-agent` 自定义 BlockParser、组件模型、JSON Schema、choice/confirm 控件；
- 交互事件、组件状态恢复；
- 插件注册（renderer/diagram/component）。

问题：

1. **核心不再是纯 Markdown**：`PuraMarkdown` 依赖 JsonSchema.Net，包体/API 面随 Agent 演进而变。
2. **插件新增语法块缺少统一入口**：`UseMarkdownExtension` 只注册 Markdig 扩展，渲染器还要手动 `UseRenderer`，且行内渲染器未接线。
3. **任意 BlockParser 与流式不兼容**：流式扫描器只理解围栏/空行，自定义块语法可能被截断。
4. **Agent 扩展 API 与核心 API 混在一起**：`IAgentComponentParser/Renderer` 既服务内置组件，也服务第三方组件；核心 API 被迫携带 Agent 概念。

---

## 2. 建议的插件语法模型（核心固定接口）

### 2.1 首选：围栏语法（Fenced）

绝大多数 Agent/图表/数学/自定义块都是 ` ```lang ` 围栏，天然被 Markdig 与流式扫描器识别。

```csharp
public interface IFenceRenderer
{
    /// <summary>诊断名称。</summary>
    string Name { get; }

    bool CanRender(string language, string source, RenderContext context);

    /// <summary>渲染围栏；流式尾部可用 context.IsTail 判断未完成状态。</summary>
    Control Render(string language, string source, RenderContext context);
}

public PuraPipelineBuilder UseFenceRenderer(IFenceRenderer renderer, int priority = 100);
```

- 按 priority 降序选择；无匹配 → 回退代码块；
- 流式期间围栏未闭合时 `context.IsTail=true`，插件应返回占位/纯文本，完成后再解析；
- 与现有 `IDiagramRenderer` 的关系：**1.0 用 `IFenceRenderer` 取代 `IDiagramRenderer`**（alpha 阶段可直接破坏），Mermaid 插件迁移；`IDiagramRenderer` 删除或仅保留一版 `[Obsolete]` 适配器。

### 2.2 行内语法（Inline）

```csharp
public interface IPuraInlineRenderer : IPuraRenderer
{
    Inline RenderInline(MarkdownObject node, RenderContext context);
}

public PuraPipelineBuilder UseInlineRenderer(IPuraInlineRenderer renderer, int priority = 100);
```

- 插件通过 `UseMarkdownExtension(IMarkdownExtension)` 注册 Markdig 解析器，再 `UseInlineRenderer` 注册渲染器；
- 必须修复当前"接口存在但 DefaultInlineRenderer 从不调用"的问题。

### 2.3 任意块级语法（Advanced）

插件通过 `UseMarkdownExtension` 注册 Markdig BlockParser，再 `UseRenderer` 注册块渲染器。

**限制**：流式扫描器无法理解自定义块边界，可能把未完成块提交/截断。1.0 建议：

- 官方支持范围：**围栏 + 行内**；
- 任意 BlockParser 标注为 **experimental**：不保证流式增量正确性（Complete 全量校正兜底），文档明确；
- 后续可增加可选契约：
  ```csharp
  public interface IStreamingBoundaryProvider
  {
      bool TryGetStableLength(string tailText, out int length);
  }
  ```
  由插件告诉扫描器"前缀到哪里可提交"。1.0 不纳入。

### 2.4 插件入口与优先级

```csharp
public interface IPuraPlugin
{
    string Name { get; }
    string Version { get; }
    string? Description { get; }
    void Configure(PuraPipelineBuilder builder);
}
```

- 插件在 `Configure` 中注册 fence/inline/block/sanitizer/highlighter；
- 同一围栏语言多个 renderer：priority 高者胜；相同 priority 按注册顺序（先注册者优先）或 fail-fast（建议后者，构建期避免歧义）；
- 插件冲突/重名继续 fail-fast（现有 `PuraConfigurationException`）。

### 2.5 安全与清洗

- 新增 `UseSanitizer(IAstSanitizer)`；`SanitizeResult.Block/AddWarning` 公开，否则自定义 sanitizer 无实际能力；
- 插件自定义语法默认视为**可信代码**；JSON 载荷（Agent）由插件自行 schema 校验；
- URL/图片仍走核心 `IUrlPolicy`。

---

## 3. Agent 插件化：是否合理？

### 3.1 结论

**合理，建议 1.0 完成。** 理由：

1. `pura-agent` 只是"围栏语言 + JSON + 组件渲染"，完全可以用 `IFenceRenderer` 承载，不需要核心自定义 BlockParser。
2. 核心剥离后不再依赖 JsonSchema.Net，包更小、启动更快，符合"高性能 Markdown 渲染"定位。
3. 插件架构需要一个真实的内置插件作为样板；Agent 是最好的证明。
4. Agent 协议可能演进（新组件、schema 版本），独立包可独立发版，不牵连核心 SemVer。
5. 宿主可自由选择：纯 Markdown 不装 Agent；聊天应用装 Agent；其他 Agent 协议可另做插件。

### 3.2 迁移后的包结构

```
PuraMarkdown                   核心：解析/渲染/流式/虚拟化/主题/本地化/插件契约
PuraMarkdown.Agent             pura-agent 围栏、JSON 组件、choice/confirm、Agent 本地化
PuraMarkdown.Highlighting.ColorCode
PuraMarkdown.Diagrams.Mermaid  迁移到 IFenceRenderer
PuraMarkdown.Chat              消息模型 + 虚拟化列表 + 流式槽（独立）
PuraMarkdown.Testing
```

依赖方向：

```
Chat ──► Core
Agent ──► Core
Mermaid/ColorCode ──► Core
```

> 包改名脚注：2026-11-10 起包身份已改名为 `PuraMarkdown.Core` / `PuraMarkdown.Agent` / `PuraMarkdown.Extension`，命名空间随包同步变更；本节为当时的设计记录，当前包清单见 [ADR-0028](adr/0028-package-identity-rename.md)。

### 3.3 哪些从核心移出

| 移出到 `PuraMarkdown.Agent` | 说明 |
| --- | --- |
| `PuraAgentBlock`、`PuraAgentBlockParser`、`PuraAgentExtension` | 改为 Agent fence renderer，不再需要自定义 AST 节点 |
| `PuraAgentComponent`/Choice/Confirm/Option/Error | 组件模型 |
| `ChoiceParser`/`ConfirmParser`/`ComponentSchemas`/`AgentJson` | JSON 解析与 schema |
| `AgentComponentRenderer`、`AgentCards`、`PuraChoiceControl`、`PuraConfirmControl` | 组件渲染 |
| `UseAgentComponents()` | 变为 Agent 包的扩展方法/插件 |
| `IAgentComponentParser/Renderer`、`PuraComponentRegistry` | 变为 Agent 包内部/插件扩展 API（第三方自定义 Agent 组件） |

### 3.4 哪些保留在核心

| 保留 | 原因 |
| --- | --- |
| `PuraInteractionEventArgs` | 通用"交互内容"事件，不绑定 Agent |
| `IPuraInteractionStateReceiver` | 核心视图负责状态恢复 |
| `PuraMarkdownOptions.InteractionEnabled` | 通用只读/交互开关 |
| `PuraMarkdownView.InteractionOccurred` | 宿主统一事件入口 |
| `IPuraLocalizer`/`PuraStringKeys` | 通用本地化；Agent 使用自己的 key 命名空间 |
| `IFenceRenderer`、`IPuraInlineRenderer`、`IPuraRenderer`、`IAstSanitizer`、`ICodeHighlighter` | 通用扩展契约 |

### 3.5 Agent 插件内部

```csharp
public sealed class AgentPlugin : IPuraPlugin
{
    public string Name => "pura.agent";
    public string Version => "1.0.0";
    public void Configure(PuraPipelineBuilder builder)
        => builder.UseFenceRenderer(new AgentFenceRenderer());
}

public sealed class AgentFenceRenderer : IFenceRenderer
{
    public bool CanRender(string language, string source, RenderContext context)
        => string.Equals(language, "pura-agent", StringComparison.OrdinalIgnoreCase);

    public Control Render(string language, string source, RenderContext context)
    {
        if (context.IsTail)
        {
            return AgentCards.Loading(...); // 流式占位
        }

        // JSON -> schema -> component renderer -> control
        // 内部仍可使用 IAgentComponentParser/Renderer（Agent 包自己的插件 API）
    }
}
```

- 流式：`pura-agent` 围栏未闭合时显示占位；闭合提交后解析 JSON。Complete 全量校正兜底。
- 状态：Agent 控件实现核心 `IPuraInteractionStateReceiver`，复用现有状态恢复。
- 安全：Agent 包负责 JSON 大小/schema；核心 `PuraLimits.MaxFenceChars` 可作为外层预算。

### 3.6 风险与代价

| 风险 | 缓解 |
| --- | --- |
| 用户升级后少装包导致 `pura-agent` 变成普通代码块 | 文档醒目；提供 `PuraMarkdown.All` 元包（可选） |
| Agent 包与核心版本不匹配 | 插件 `MinCoreVersion`/构建期校验；统一版本号 |
| 自定义组件 API 从核心移到 Agent 包 | alpha 阶段直接迁移；文档重新归类 |
| 任意 BlockParser 流式风险 | 1.0 只承诺围栏/行内；Agent 本身是围栏 |
| 核心测试/示例需引用 Agent 包 | 调整项目引用；Agent 测试独立 |

---

## 4. Chat 包（独立）

```csharp
public sealed class PuraChatMessage
{
    public string Id { get; init; }
    public PuraMessageRole Role { get; init; }
    public string Markdown { get; set; }
    public PuraMessageStatus Status { get; set; }
}

public sealed class PuraChatView : ContentControl
{
    public ObservableCollection<PuraChatMessage> Messages { get; }
    public PuraMarkdownOptions Options { get; init; }
    public PuraStreamingSession BeginStreamingMessage(string? id = null);
    public void ScrollToMessage(string id);
}
```

- 消息级虚拟化、流式槽、自动滚动、状态恢复；
- 不依赖 Agent；使用 Agent 时宿主自行注册插件；
- 核心保持单 `PuraMarkdownView`。

---

## 5. 建议的 1.0 实施顺序

1. 核心插件契约：`IFenceRenderer`（取代 `IDiagramRenderer`）、`UseInlineRenderer`、`UseSanitizer`、优先级/冲突规则。
2. 新建 `PuraMarkdown.Agent`：迁移 Agent 文件，`AgentPlugin` 用 `IFenceRenderer` 重写 `pura-agent` 解析。
3. 核心删除 Agent 相关 API/依赖；Demo/测试改为引用 Agent 包。
4. Mermaid 插件迁移到 `IFenceRenderer`。
5. 新建 `PuraMarkdown.Chat`（消息虚拟化 + 流式槽）。
6. API 收敛（P0 死配置删除、internal 化、视图生命周期）。
7. 1.0 冻结：`PublicAPI.Shipped.txt`、SemVer、迁移说明。

---

## 6. 待确认

1. **是否确认 1.0 把 Agent 移为独立插件包？**（建议：是）
2. **1.0 是否用 `IFenceRenderer` 取代 `IDiagramRenderer`？**（建议：是；alpha 直接替换）
3. **1.0 插件语法范围是否只承诺"围栏 + 行内"，任意 BlockParser 标为 experimental？**（建议：是）
4. **Chat 包命名 `PuraMarkdown.Chat` 是否合适？**（建议：是）
5. **Agent 组件扩展 API（`IAgentComponentParser/Renderer`）是否随 Agent 包一起移动、作为 Agent 插件作者的稳定 API？**（建议：是）

