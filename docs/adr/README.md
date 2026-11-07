# 架构决策记录（ADR）

记录已做出的、影响面大的架构决策。格式：背景 → 决策 → 理由 → 后果 → 备选。状态取值：提议 / 已接受 / 已废弃 / 被取代。

## 当前有效

| 编号 | 标题 | 状态 |
| --- | --- | --- |
| [ADR-0006](0006-interaction-events-not-transport.md) | 交互结果只产出事件与建议文本，不内置传输协议 | 有效 |
| [ADR-0009](0009-html-policy-disabled.md) | 1.0 不渲染 HTML、不提供 HTML 子集 | 有效 |
| [ADR-0010](0010-streaming-append-only.md) | 流式输入只接受 append，不提供编辑与重写 | 有效 |
| [ADR-0011](0011-markdig-ast-direct-to-avalonia.md) | 渲染直接消费 Markdig AST、产出 Avalonia 控件（无 IR） | 已接受（2026-11-05） |
| [ADR-0012](0012-pura-agent-fence.md) | `pura-agent` 围栏 + JSON 作为扩展语法主路径 | 已接受（2026-11-05） |
| [ADR-0013](0013-net10-target.md) | 目标框架升级为 net10.0 | 已接受（2026-11-05） |
| [ADR-0014](0014-plugins-for-diagrams-and-highlighting.md) | 图表/高亮/公式外置为插件契约 | 已接受（2026-11-05） |
| [ADR-0015](0015-streaming-block-dirty-grafting.md) | 流式采用「块级脏检查 + 尾部窗口 + 视觉树嫁接」 | 已接受（2026-11-05） |
| [ADR-0016](0016-avalonia-native-no-platform-abstraction.md) | 1.0 只交付 Avalonia 原生控件，多平台后置 | 已接受（2026-11-05） |
| [ADR-0020](0020-no-ir-in-1.0.md) | 1.0 不引入 IR（保留 Markdig AST） | 已接受（2026-11-05，选项 A） |
| [ADR-0021](0021-no-virtual-render-tree-in-1.0.md) | 1.0 不引入平台无关虚拟渲染树 | 已接受（2026-11-05，选项 A） |
| [ADR-0022](0022-no-platform-adapter-layer-in-1.0.md) | 跨平台由 Avalonia 承担，不建渲染适配层 | 已接受（2026-11-05，选项 A） |
| [ADR-0023](0023-mermaid-engine-mermaider.md) | Mermaid 引擎选择 Mermaider（MIT，纯 .NET）；Naiad 因 EULA 排除 | 已接受（2026-11-06） |
| [ADR-0024](0024-agent-as-plugin.md) | Agent 剥离为独立语法插件，核心只做 Markdown | 已接受（2026-11-07） |
| [ADR-0017](0017-block-virtualization.md) | 块级虚拟化采用可选 ItemsRepeater 模式 | 已接受（2026-11-07） |

## 已取代（保留原文，便于追溯）

| 编号 | 标题 | 被谁取代 |
| --- | --- | --- |
| [ADR-0001](0001-self-built-ir-over-html.md) | 在 Markdig 之上自建 IR 与渲染层 | ADR-0011 |
| [ADR-0002](0002-extension-syntax-carrier.md) | Agent 扩展语法采用 `:::` 容器 + 数据载荷 | ADR-0012 |
| [ADR-0003](0003-render-tree-shape.md) | 渲染树使用固定语义元素枚举 + 组件宿主节点 | ADR-0011 |
| [ADR-0004](0004-streaming-incremental-strategy.md) | 流式渲染采用「稳定块提交 + 尾部窗口重解析」 | ADR-0015 |
| [ADR-0005](0005-target-framework.md) | 目标框架采用 net8.0 单目标 | ADR-0013 |
| [ADR-0007](0007-chart-and-math-engines.md) | 图表与公式：核心只定义数据契约 | ADR-0014 |
| [ADR-0008](0008-platform-avalonia-first.md) | 1.0 平台首选 Avalonia 12 桌面 | ADR-0016 |

## 待定

| 编号 | 主题 | 阻塞于 |
| --- | --- | --- |
| ADR-0018 | 数学公式插件（CSharpMath vs 预渲染 SVG） | 1.1 需求确认 |
| ADR-0019 | 容器指令插件（`:::choice`）是否官方提供 | M3 社区反馈 |

> 2026-11-05 设计阶段收官：全部架构决策已接受（共 13 项有效决策），不再有"提议"状态的 ADR。

## 记录约定

- 一个 ADR 只回答一个问题；被推翻时保留原文并把状态改为「被取代」，另写新 ADR。
- ADR 编号不复用；与需求编号（FR-xxx）、诊断码（PURA-xxx）分开。
- 「用户决策」类 ADR 记录决策来源与日期；变更需新 ADR 显式取代。


