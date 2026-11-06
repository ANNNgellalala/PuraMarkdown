# ADR-0002：Agent 扩展语法采用 `:::` 容器 + 数据载荷，并提供围栏兼容模式

- 状态：**被 ADR-0012 取代**（2026-11-05：主路径改为 `pura-agent` 围栏 + JSON，容器指令降为插件）
- 日期：2026-11-04

## 背景

需要一种能在 Markdown 中表达"交互卡片/图表/工具调用"的语法，且满足：可降级、流式安全、边界明确、数据可校验、对 LLM 生成友好。

候选：

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A. `:::name{attrs}` … `:::` 自定义容器 | 正文与数据可分；可嵌套；降级为普通文本 | 依赖 Markdig CustomContainers；语法壳在标准渲染器中可见 |
| B. 围栏代码块 ` ```pura-choice{...} ` + JSON | 标准 CommonMark，降级最干净 | 无法承载 Markdown 正文；交互列表可读性差 |
| C. 原始 HTML | 表达力强 | XSS、不可移植、流式坏 DOM |
| D. 自研 fenced 语法（如 `<<<choice>>>`） | 完全可控 | 需自研 BlockParser；降级形态不友好 |

## 决策

采用 **A 为主**：`:::name{attrs}` 容器，正文区为 Markdown，可选 ` ```json ` 载荷区；**B 作为兼容模式**（`pfm.compat=code-fence`），用于"降级优先"的场景。

## 理由

- 正文 Markdown + JSON 载荷的组合让"可读降级"和"结构化渲染"同时成立（降级时正文仍可读）。
- `:::` 是 Markdig 原生支持的容器语法（`Markdig.Extensions.CustomContainers`），避免自研解析器；同时该语法在标准 CommonMark 中表现为普通文本，不破坏阅读。
- 组件名 + 属性字符串足以表达组件实例，载荷 JSON 负责数据，两者职责清晰。
- 交互列表另提供 GFM 任务列表简写（`- [ ] value | label | description`），兼顾 token 效率与可读性。

## 后果

- 已实测：`Markdig.Extensions.CustomContainers` 支持可变长度围栏与嵌套（外层须更长），节点为 `CustomContainer`（`Info`/`Arguments`/`Span`/子块）。
- 属性解析**不依赖** Markdig GenericAttributes（有单字符 class 失败、属性被吞等 bug；且其"裸词 = class"语义与本项目"裸键 = 布尔 true"冲突），由 Pura 按 `CustomContainer` 的 `Span` 取首行原文自行解析。因此**规范形式是 `:::name{attrs}`，而不是 Markdig 惯用的 `::: {.name #id}`**——这是有意偏离 Markdig 惯例以保住语义一致性。
- 未闭合 `:::` 会把后文吞入容器 → 流式必须依赖尾部窗口（ADR-0004）。
- 语法壳在标准渲染器中可见，需要通过文档告知"这是扩展语法"，并在导出时提供规范化。

## 相关

- 语法规范 `docs/03-agent-syntax-spec.md` §2–§3、§11 Q1。
