# ADR-0024：Agent 剥离为独立语法插件，核心只做 Markdown

- 状态：已接受（2026-11-07）；**打包部分被 ADR-0027 调整**（Agent 内容并入核心包 `PuraMarkdown`，仅保留独立命名空间 `PuraMarkdown.Agent`，不再是独立包）
- 关联：ADR-0014（图表/高亮外置）、[插件通用接口与 Agent 插件化讨论](../24-插件通用接口与Agent插件化讨论.md)、[实现记录](../25-Agent插件化与公共API收敛实现.md)

## 背景

核心 `PuraMarkdown` 同时承担 Markdown 渲染与 `pura-agent` 协议（自定义 BlockParser、组件模型、JSON Schema、choice/confirm）。这导致：

- 核心依赖 `JsonSchema.Net`，包体与 API 面随 Agent 演进而变；
- 插件新增语法块缺少统一围栏契约（`IDiagramRenderer` 只覆盖图表）；
- 行内渲染器 `IPuraInlineRenderer` 存在但未接线；
- Agent 与核心版本耦合。

## 决策

1. 核心新增 **`IFenceRenderer`**，接管 ` ```lang ` 围栏（图表/Agent/数学/自定义块），取代 `IDiagramRenderer`。
2. `pura-agent` 从核心剥离为独立包 **`PuraMarkdown.Agent`**，以 `AgentPlugin` 注册 `AgentFenceRenderer`；不再使用自定义 Markdig BlockParser。
3. 核心保留通用交互能力：`PuraInteractionEventArgs`、`IPuraInteractionStateReceiver`、`InteractionOccurred`、`InteractionEnabled`、本地化。
4. Agent 包负责 JSON 组件模型/Schema/choice/confirm/状态恢复；未安装插件时 `pura-agent` 回退代码块。
5. Chat 控件独立为 **`PuraMarkdown.Chat`**，核心保持单控件渲染职责。

## 理由

- 围栏语法天然被 Markdig 与流式扫描器识别，Agent 插件化无需自定义块解析；
- 核心不再依赖 JsonSchema.Net，定位更纯粹；
- 插件架构需要真实内置插件作为样板；
- Agent 协议可独立演进、独立发版；
- 宿主可按需装配：纯 Markdown 不装 Agent，聊天应用装 Agent + Chat。

## 后果

- **破坏性变更**（alpha 允许）：`UseAgentComponents`/Agent 类型迁移到新包；`IDiagramRenderer`/`UseDiagramRenderer` 被 `IFenceRenderer`/`UseFenceRenderer` 取代；`UseLimits`/`Logger`/`EnableBlockReuse`/`IPuraPlatformServices` 删除。
- 用户需额外引用 `PuraMarkdown.Agent` 并调用 `UsePlugin(new AgentPlugin())`（或 `UseAgentComponents()` 扩展）。
- Agent 包与核心版本需保持一致（统一版本号 + 后续 MinCoreVersion 校验）。
- 流式 `pura-agent` 围栏在未闭合时显示占位，闭合后解析；Complete 全量校正兜底。
- 自定义 Agent 组件通过 `AgentPlugin(parsers, renderers)` 扩展；多个插件共同扩展同一围栏的组合式注册留待 1.1。
- Mermaid 插件迁移到 `IFenceRenderer`。

## 备选

- 保留 Agent 在核心：实现简单，但核心持续膨胀，插件架构缺乏验证（未采用）。
- 仅拆程序集不拆 API：仍保留组件注册表与 BlockParser，边界不清晰（未采用）。
- 任意 BlockParser 插件作为 1.0 稳定能力：流式边界不可控，标为 experimental（未纳入稳定面）。
