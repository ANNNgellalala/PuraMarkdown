# ADR-0001：在 Markdig 之上自建 IR 与渲染层，不复用 HTML 输出

- 状态：**被 ADR-0011 取代**（2026-11-05：改为 Markdig AST 直连 Avalonia，取消 IR）
- 日期：2026-11-04
- 决策者：架构组

## 背景

PuraMarkdown 需要把 Agent 输出的 Markdown 渲染进宿主 UI，并支持交互组件、流式增量、安全策略与多平台适配。候选：

1. Markdig → HTML 字符串 → WebView/浏览器控件；
2. Markdig → 自建 UI 无关 IR → 虚拟渲染树 → 平台适配器；
3. 直接使用现成的 Markdown 控件（Markdig.Wpf、Markdown.Avalonia 等）。

## 决策

采用方案 2。**HTML 仅作为导出格式**（`PuraMarkdown.Export`），不作为内部渲染路径。

## 理由

- HTML 直出无法承载"交互卡片 + 结构化回传 + 状态机"；把交互编码进 HTML（`<input>`/`<button>`）需要放开原始 HTML，XSS 面不可接受。
- 流式场景下 HTML 字符串拼接会产生坏 DOM，且无法做块级 diff。
- 现成控件（方案 3）以"渲染静态文档"为目标，未暴露扩展节点、增量更新、可替换渲染器的契约；二次开发成本高于自建（见生态调研）。
- UI 无关 IR 使同一份解析结果可在 Blazor/Avalonia/导出/测试间复用，并可用快照测试锁定行为。

## 后果

- 需要自行实现 IR 构建、渲染规划、diff 与适配器（工作量 +30%）。
- 需要维护 IR 的版本与快照兼容（FR-105）。
- 换来：安全、可移植、可测试、可扩展。

## 备选与回退

若 M1 发现 IR 设计无法覆盖 CommonMark 语义（如嵌套引用+列表的复杂结构），可回退为"Markdig AST 包装层"，但仍保留虚拟渲染树与适配器。
