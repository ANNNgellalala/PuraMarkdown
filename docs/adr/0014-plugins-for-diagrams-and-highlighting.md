# ADR-0014：图表/高亮/公式外置为插件契约

- 状态：已接受（取代 ADR-0007）
- 日期：2026-11-05
- 关联：插件契约 `04-plugin-and-rendering.md`、需求 FR-402/403/405

## 背景

v0.2 决定：核心定义 `ChartSpec`，Avalonia 适配器用 ScottPlot 5 + CSharpMath。新需求方案主张：图表完全外置——核心只识别围栏并路由给 `IDiagramRenderer` 插件；代码高亮作为可注入策略。

## 决策

1. **核心不定义图表 schema**，也不引用任何图表/高亮引擎。
2. 图表：核心识别 ` ```mermaid `、` ```pura-chart ` 等围栏，按注册顺序询问 `IDiagramRenderer.CanRenderDiagram(language, source)`；无插件/失败 → 回退代码块。
3. 高亮：`ICodeHighlighter` 注入；默认纯等宽文本；官方可选包提供 ColorCode/TextMate 实现。
4. 公式：不作为 1.0 范围；1.1 以插件（CSharpMath）形式提供。
5. `pura-chart` 的 JSON schema 由对应插件包定义；若需要跨插件统一格式，由官方插件包定义并文档化，不进核心。

## 理由

- 图表引擎差异大（Mermaid/ScottPlot/Naiad/WebView），演进快；核心绑定 schema 会限制插件与用户选择。
- 用户明确决策"图表与公式核心只定义契约"（此前为 ChartSpec；**本次按新方案收敛为"围栏 + 插件契约"**，schema 下放到插件）。
- 核心零引擎依赖 → 包体积小、许可干净、AOT 友好；无插件时回退代码块，信息不丢。
- 高亮同理：默认零依赖，重型引擎（TextMate/AvaloniaEdit）由用户按需引入。

## 后果

- 宿主/插件需自行约定图表数据格式；文档提供 `pura-chart` 参考 schema（插件包内）。
- `IDiagramRenderer` 签名只含 `(language, source)`，无法传递强类型数据；插件自行解析 source（JSON/DSL）。这是有意的"窄契约"。
- 之前"ScottPlot + CSharpMath 进核心"的决策作废；ScottPlot 仅作为可选插件候选（M3）。

## 备选

若插件生态出现"需要统一图表 AST"的强需求，再考虑在 `PuraMarkdown.Diagrams` 聚合包中定义 `PuraChartSpec`（不进核心）。
