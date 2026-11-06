## 一、需求澄清：PuraMarkdown 要做什么，不做什么

### 1.1 明确要做的事

**只读渲染宿主**。控件消费 Markdown 文本，输出 Avalonia 原生视觉树，不提供对原始 Markdown 文本的编辑入口。用户与内容的交互仅限于 Agent 交互组件（单选、多选）的选择动作，以及链接点击、代码块复制等查看类交互。

**Agent 扩展语法的解析与路由**。Markdig 负责标准 Markdown 的解析，PuraMarkdown 在其上追加一层**Agent 扩展语法识别**，将特定语法形态的节点路由给对应的渲染插件，而不是让 Markdig 直接理解“这是一个单选列表”。

**基础渲染的内置默认实现**。标准 Markdown 元素（段落、标题、列表、代码块、引用、表格、链接、图片）必须有开箱即用的 Avalonia 原生渲染，不依赖任何外部插件即可正常工作。

**插件化的可替换渲染策略**。每一种“可被扩展的元素类型”都对应一个可注册、可覆盖的渲染器契约。用户可以在基础渲染之上替换代码高亮引擎、接入 Mermaid 图表库、或定义全新的 Agent 组件。

**Agent 交互组件的视觉呈现与状态上报**。单选/多选列表渲染为可点击的原生 Avalonia 控件，用户的选择通过事件回调上报给宿主应用，由宿主决定是否将选择结果回传给 Agent。PuraMarkdown **不负责**发送消息或管理对话状态。

**流式友好的增量更新接口**。提供 `Append` 式的流式输入路径，使 Agent 的逐字输出不必每次都触发全量重解析与全量视觉树重建。

### 1.2 明确不做的事

不实现源码感知编辑、命中测试到源码位置的映射、编辑会话隔离——这些是 ProMarkdown 的核心领域，对只读对话渲染是过度设计。

不试图在 Markdig 管线内部从头实现语法高亮引擎——上一轮已讨论过，高亮应作为独立策略注入。

不绑定特定的 Mermaid 渲染实现。图表渲染是一个典型的“外部组件”场景，PuraMarkdown 只定义**图表渲染契约**，具体用哪个引擎由插件决定。

不内置对话状态管理、消息历史、Agent 通信协议。PuraMarkdown 是**视图层组件**，不是对话框架。


## 二、总体架构：四层分离

PuraMarkdown 的架构可以划分为四个职责清晰的层次，层与层之间通过契约通信，而不是直接依赖具体实现。

**第一层：解析路由层（Parse & Route）** 。Markdig 作为标准 Markdown 的解析引擎，通过自定义的 `BlockParser` / `InlineParser` 识别 Agent 扩展语法，生成自定义的 `Block` / `Inline` 节点。这一层的输出是一棵混合了标准节点和自定义节点的 Markdig AST。

**第二层：节点分发层（Node Dispatch）** 。遍历 Markdig AST，对每个节点查询注册表：是否存在针对该节点类型（标准类型或自定义类型）的渲染器？如果存在，交给对应的渲染器；如果不存在，回退到基础默认渲染器。这一层是**插件化的核心接缝**。

**第三层：渲染执行层（Render）** 。渲染器接收 Markdig 节点和上下文，产出一棵 Avalonia 视觉树（或增量更新指令）。基础渲染器产出 `TextBlock`、`Border`、`StackPanel` 等原生控件；插件渲染器可以产出任何 Avalonia 控件，包括图表控件、交互列表控件。

**第四层：流式生命周期层（Streaming Session）** 。包裹整个渲染管线，处理追加文本、脏区域判定、增量重渲染与视觉树嫁接。只读场景下，这一层可以比编辑器场景简单得多：只需要“哪些块发生了变化”，而不需要“哪个字符位置对应哪个源码区间”。


## 三、Agent 扩展语法的设计

这是 PuraMarkdown 区别于通用 Markdown 控件的关键。搜索到的 Agent Markdown 实践中存在两种主流语法形态，值得作为设计参考。

**围栏携带结构化数据**。A2UI 的做法是在 ` ```a2ui ` 围栏内放置一个 JSON 对象，由渲染方解析并绘制对应组件。这种形态的优点是：语法对 Markdig 而言只是一个普通的 fenced code block，识别成本极低；结构化数据的 schema 可以独立演进，不需要不断扩展 Markdown 语法本身。缺点是 Markdown 源码的可读性不如专用指令直观。

**容器指令块（directive block）** 。agent-md 使用 `::choice ... ::` 这样的容器指令语法来声明交互组件，支持嵌套，并配有 schema 校验。这种形态更“像 Markdown”，可读性好，但需要自定义 `BlockParser` 来识别，且嵌套语法的解析复杂度更高。

对于 PuraMarkdown，建议**两种都支持，但以围栏携带结构化数据为优先路径**。理由如下：

围栏语法让 Markdig 的自定义解析负担最小。你只需要注册一个针对特定 `Info` 的 `FencedCodeBlock` 处理逻辑，而不必实现一套嵌套容器解析器。

JSON schema 的校验、降级和错误报告可以集中在一个独立的 validator 中，与 Markdig 解析解耦。A2UI 的实践也表明，用一个独立的 schema 模块同时服务“解析、校验、降级”是可行的。

Agent 生成围栏 + JSON 比生成容器指令更稳定，因为模型对 fenced code block 的格式约束感知更强。

具体语法形态建议：

```
```pura-agent
{ "type": "choice", "question": "请选择部署环境", "multi": false, "options": [...] }
```
```

`pura-agent` 作为 `Info` 标识，PuraMarkdown 在解析层拦截所有 `pura-agent` 围栏，将内容交给 `IAgentComponentParser` 解析为 `PuraAgentBlock`（继承自 Markdig 的 `LeafBlock`）。这个 `PuraAgentBlock` 携带解析后的组件描述对象（或解析错误信息）。

如果用户确实偏好容器指令语法，可以通过插件注册自定义 `BlockParser` 来实现 `::choice` 形态，但这不应作为基础包的必选项。


## 四、插件契约设计

插件化的关键是**契约足够窄，但组合足够灵活**。参照 ProMarkdown 将扩展职责拆分为 parser、renderer、template 等独立契约的做法，PuraMarkdown 可以定义以下核心接口。由于只读场景不需要编辑契约，这里的契约集比 ProMarkdown 更精简。

### 4.1 渲染器契约

```csharp
public interface IPuraRenderer
{
    bool CanRender(MarkdownObject node, RenderContext context);
    Control Render(MarkdownObject node, RenderContext context);
}
```

`CanRender` 负责声明该渲染器能处理哪些节点类型。`Render` 产出 Avalonia 控件。对于需要增量更新的节点，可以实现一个可选的 `IIncrementalPuraRenderer`，暴露 `Update(Control existing, MarkdownObject node)` 方法。

### 4.2 插件注册契约

```csharp
public interface IPuraPlugin
{
    string Name { get; }
    void Configure(PuraPipelineBuilder builder);
}
```

`PuraPipelineBuilder` 暴露三类注册入口：**解析器注册**（向 Markdig 管线添加自定义 `BlockParser` / `InlineParser`）、**渲染器注册**（向渲染器注册表添加 `IPuraRenderer`）、**Agent 组件解析器注册**（向 `pura-agent` 围栏的内容处理器添加 `IAgentComponentParser`）。

### 4.3 Agent 交互组件的契约

交互列表是 PuraMarkdown 的一等公民，需要单独的契约来定义“选中”行为如何上报。

```csharp
public interface IAgentComponentRenderer : IPuraRenderer
{
    event EventHandler<AgentComponentEventArgs> InteractionOccurred;
}
```

渲染器产出的控件内部维护选择状态，当用户点击选项时触发 `InteractionOccurred`，携带组件 ID、选中项、是否多选等信息。PuraMarkdown 控件本身只负责**转发**这个事件，不决定后续行为。宿主应用可以选择禁用交互（只读模式）、或将结果填入输入框、或直接回传给 Agent。

### 4.4 渲染上下文

`RenderContext` 应当携带对**服务定位**的访问，而不是硬编码具体服务。至少需要暴露：主题资源（用于样式令牌）、图片加载器、链接打开处理器、以及一个可选的**同步上下文**（用于流式更新时判断是否在 UI 线程）。这使得插件可以在不直接依赖 PuraMarkdown 内部类型的情况下完成渲染。


## 五、基础渲染的“默认组”设计

基础渲染器不应是一堆散落在各处的 `if-else`，而应是一组**可被单独覆盖的默认策略**。

**标准 Markdown 块级元素**：段落 → `TextBlock`（或 `SelectableTextBlock`）；标题 → 带样式类的 `TextBlock`；列表 → `StackPanel` + 项目符号/编号；引用 → 带左边框的 `Border` 包裹；表格 → `Grid` 或专用表格控件；代码块 → 一个 `CodeBlockControl` 容器，内部默认使用**纯等宽文本**渲染，高亮作为可替换策略注入；分隔线 → `Rectangle`。

**标准内联元素**：强调/加粗/删除线 → 对应 `Run` 的字体属性；内联代码 → 带背景色的 `Run`；链接 → 可点击的 `Run` 或 `HyperlinkButton`；图片 → 异步加载的 `Image` 控件。

**Agent 交互组件默认实现**：`choice` 类型渲染为一组 `RadioButton`（单选）或 `CheckBox`（多选），外层包裹 `Border` + `StackPanel`，问题文本作为标题。默认实现应当处理**禁用状态**（只读展示时不可交互）和**降级状态**（schema 解析失败时显示源码和错误摘要，参照 A2UI 的“解析不了的块显示源码，并写明第一处错误”的做法）。

基础渲染器与插件渲染器在注册表中**处于同一层级**。基础渲染器不是“硬编码的特权路径”，而是以较低优先级注册的默认项。当插件注册了一个 `CanRender` 条件更具体的渲染器时，它覆盖基础默认项。这保证了“基础可用”和“可替换”的统一。


## 六、流式渲染策略

只读对话场景下的流式渲染，核心问题不是编辑场景中的“源码映射”，而是**如何避免对未变化的块做重复的视觉树重建**。

Avalonia 官方 Markdown 控件的 `MarkdownStreamingSession` 提供了一个值得借鉴的思路：`Append` 是线程安全的，解析、diff、snapshot 构建在后台线程完成，只有最终的树嫁接在 UI 线程执行。LiveMarkdown.Avalonia 的 `MarkdownNode` 系统则通过 `SourceSpan` 追踪和脏检查来判断哪些节点需要更新。

对于 PuraMarkdown，建议采用**块级脏检查 + 增量嫁接**的简化策略：

文本追加时，不立即触发全量重解析。维护一个内部的**块快照列表**（每个块对应一个 Markdig `Block` 的浅层标识和其源码跨度）。新追加的文本只可能影响**最后一个块**（或最后几个块，当追加内容包含块级分隔符时）。

当最后一个块的源码跨度发生变化时，只重新解析该块，并请求对应的渲染器更新或重建该块的视觉子树。之前的块保持不变，其视觉控件继续存在于面板中。

对于 `pura-agent` 围栏，流式过程中围栏内容可能不完整（JSON 未闭合）。解析器应将未完成的围栏标记为 `Incomplete` 状态，渲染器显示一个**占位符**（例如“组件加载中…”），而不是报错。当围栏闭合且 JSON 解析成功后，再替换为完整的交互组件视觉树。A2UI 也采用了“流式输出期间组件块显示占位，落定后一次绘出”的策略。

这一策略的前提是**PuraMarkdown 的视觉树以块为单位组织**（例如一个纵向 `StackPanel`，每个块对应一个子容器）。如果采用富文本文档模型（如 `FlowDocument`），增量嫁接的粒度会更细，但实现复杂度也更高。对于 Agent 对话场景，块级粒度通常已经足够。


## 七、图表渲染的插件边界

图表不应进入 PuraMarkdown 的核心。核心只定义**图表围栏的识别规则**（例如 ` ```mermaid ` 或 ` ```pura-chart `），并将其路由给已注册的图表渲染插件。

插件的最小契约可以简化为：

```csharp
public interface IDiagramRenderer
{
    bool CanRenderDiagram(string language, string source);
    Control RenderDiagram(string language, string source, RenderContext context);
}
```

PuraMarkdown 在遇到图表围栏时，遍历已注册的 `IDiagramRenderer`，选择第一个 `CanRenderDiagram` 返回 `true` 的渲染器。如果没有插件注册，则回退到基础代码块渲染，显示原始图表源码——这保证了“没有插件时不会崩溃，只是不渲染图表”。

具体选择哪个引擎是实现细节：LiveMarkdown.Avalonia 通过独立 NuGet 包 `LiveMarkdown.Avalonia.Mermaid` 接入 Mermaider；lucidview 使用 Naiad 进行原生 .NET 渲染。PuraMarkdown 的插件模型允许用户根据性能、图表类型覆盖度和分发体积自行选择，而不是在核心中锁定一个引擎。


## 八、错误处理与降级策略

只读 Agent 对话中，渲染失败不应导致整个消息无法阅读。分层降级是必须的：

**Agent 组件解析失败**：在组件位置显示一个带错误摘要的 `Border`，内部展示原始围栏源码。错误摘要包含第一处 schema 错误的位置和原因，与 A2UI 的检查脚本思路一致。

**插件渲染器抛出异常**：捕获异常，回退到基础渲染器。如果基础渲染器也失败，显示纯文本源码。整个回退过程对宿主应用报告一个非致命警告事件，而不是让异常冒泡到 UI 线程导致崩溃。

**流式过程中的不完整语法**：对于未闭合的围栏、未闭合的强调标记等，解析器应尽量容忍并产出可用的部分结果。Markdig 本身对不完整输入有一定容忍度，但自定义解析器需要显式处理 `Incomplete` 状态，避免将流式中间态当作错误处理。


## 九、与 ProMarkdown / LiveMarkdown 的边界对照

| 维度 | PuraMarkdown 的取舍 |
| :--- | :--- |
| **编辑能力** | 不做。只读宿主，无源码映射、无编辑会话、无命中测试到源码 |
| **插件粒度** | 比 ProMarkdown 更窄：只保留解析、渲染、Agent 组件解析、图表四类契约 |
| **流式优化** | 块级脏检查 + 增量嫁接，不追求字符级增量；复杂度远低于 LiveMarkdown 的节点系统 |
| **Agent 交互** | 一等公民。交互事件是核心输出，而非附加功能 |
| **图表** | 完全外置。核心只识别围栏，渲染由插件决定 |
| **基础渲染** | 内置但可覆盖，不是特权路径，而是注册表中的默认优先级项 |


## 十、建议的落地顺序

**第一步**：实现**解析路由层 + 基础渲染器 + 块级视觉树组织**。让标准 Markdown 在 Avalonia 中正确渲染，并建立“AST 节点 → 渲染器 → 控件”的分发骨架。这一步不涉及任何插件，但骨架必须允许注册覆盖。

**第二步**：实现 **`pura-agent` 围栏拦截 + `choice` 组件的默认渲染与事件上报**。这是 PuraMarkdown 的差异化核心，应尽早验证交互契约是否满足宿主应用的需求。

**第三步**：引入**插件注册机制**（`IPuraPlugin` / `PuraPipelineBuilder`），将代码高亮和图表渲染作为第一批插件验证契约的完备性。如果图表插件的 `IDiagramRenderer` 契约在接入一个真实引擎（如 Mermaider 或 Naiad）时不需要修改，说明边界设计是合理的。

**第四步**：实现**流式会话**。在已有块级视觉树组织的基础上，加入脏检查和增量嫁接。流式路径应当作为一条**独立的输入通道**存在（例如 `StreamingSession.Append`），而不是让宿主应用自己反复设置 `Text` 属性并依赖全量重解析。


总结而言，PuraMarkdown 的设计重点是三个清晰边界：**解析与渲染的边界**（Markdig 只解析，渲染器只渲染）、**核心与插件的边界**（Agent 组件与图表都是可替换策略）、**视图与对话状态的边界**（PuraMarkdown 上报交互，不管理对话）。守住这三条边界，控件就能在保持只读渲染简洁性的同时，获得足够的扩展空间来适配不同 Agent 应用的需求。