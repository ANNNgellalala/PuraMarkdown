# ADR-0012：`pura-agent` 围栏 + JSON 作为扩展语法主路径

- 状态：已接受（取代 ADR-0002）
- 日期：2026-11-05
- 关联：语法规范 `03-agent-syntax-spec.md`

## 背景

v0.2 以 `:::choice{...}` 容器指令为主路径，围栏 JSON 作为兼容模式。新需求方案主张：围栏 + 结构化 JSON 为优先路径，容器指令由插件实现。

## 决策

1. **主路径**：` ```pura-agent ` 围栏，内容为单个 JSON 对象；解析为 `PuraAgentBlock : LeafBlock`。
2. JSON 的 `type` 决定组件类型（内置 `choice`、`confirm`），`version` 决定 schema 版本。
3. **容器指令**（`:::choice`）不作为核心语法，由插件通过自定义 `BlockParser` 实现。
4. 核心不解析图表 JSON；` ```mermaid ` / ` ```pura-chart ` 只由 `IDiagramRenderer` 插件处理。

## 理由

- Markdig 自带 fenced code block 解析；自定义 `BlockParser` 只需识别 Info，不必实现嵌套容器与属性解析（v0.2 已知 Markdig 容器属性存在 bug，需要绕过）。
- JSON Schema 校验、错误定位、降级可集中在一个独立模块；schema 可独立演进（加字段不需要改 Markdown 语法）。
- Agent 对围栏格式的遵循度高于嵌套容器；生成更稳定。
- 降级为普通代码块，通用渲染器中信息零丢失（v0.2 的容器语法在通用渲染器中是普通文本，也可读但语义更弱）。

## 后果

- 组件可读性不如容器指令（JSON 冗长）；通过 `question`/`label` 等字段改善。
- 流式时 JSON 半截不可解析 → 必须显示占位，闭合后再渲染（已写入语法规范 §3.4）。
- 迁移成本：v0.2 的 11 个组件（callout/tool/thinking/diff/…）不再内置；如需要，以插件组件类型或普通 Markdown 表达。
- 插件作者可用容器语法作为替代语法，但需自行保证与围栏语义一致。

## 备选

容器语法作为插件（M3 示例）验证"换语法外壳、复用组件模型"的可行性。
