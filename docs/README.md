# 文档索引

## 当前有效（应用接入优先）

| 文档 | 内容 |
| --- | --- |
| [应用集成指南](26-应用集成指南.md) | 包选择（Core/Agent/Extension）、最小接入、Agent/Chat、流式、常见坑 |
| [迁移指南（0.1.0-alpha.1）](27-迁移指南-0.1.0-alpha.1.md) | 从旧版（Agent 在核心、`IDiagramRenderer`）升级；含 7 个包 → 3 个包、旧包 → 新包与 `using` 迁移 |
| [公共 API 参考](28-公共API参考.md) | 宿主/插件/Chat API 签名、包 → 命名空间映射（一一对应）与稳定性分级 |
| [版本与升级策略](29-版本与升级策略.md) | Core/Agent/Extension 三包依赖矩阵、API 稳定性分级、APP 升级步骤 |
| [Agent 与 Chat 能力增强](30-Agent与Chat能力增强.md) | 会话模型/快照/流式状态，Agent 状态持久化/组件目录 |
| [语法与渲染分离讨论](31-语法与渲染分离讨论.md) | 类 IDE/LSP 的 Syntax/Renderer 拆分方案与迁移路径 |
| [技术演进路径 B→C](32-技术演进路径-B到C.md) | B（Markdig AST 分离）工作包、验收与 C 过渡 |
| [B 阶段实施记录](33-B阶段实施记录.md) | B0–B5 落地内容、验证与剩余硬化项 |
| [B 阶段基线：API/Trim/性能](34-B阶段基线-API-Trim-性能.md) | PublicAPI baseline（Core/Agent/Extension）、Trim/AOT 结果、性能对比表 |
| [Markdig 支持节点子集与兼容矩阵](35-Markdig支持节点子集与兼容矩阵.md) | 节点矩阵、版本契约、升级检查清单 |
| [Markdown 功能覆盖与缺口](36-Markdown功能覆盖与缺口.md) | 已支持特性、未实现语法/渲染、优先级 |
| [Emoji/自定义容器/数学公式支持评估](37-Emoji-自定义容器-数学公式支持评估.md) | 三项能力的用户效果、实现面、工作量与风险 |
| [ADR 索引](adr/README.md) | 架构决策记录（含 [ADR-0027 包整合](adr/0027-package-consolidation.md)、[ADR-0028 包改名](adr/0028-package-identity-rename.md)） |

## 历史设计与里程碑（实现可能已过时）

以下文档记录设计/开发过程，代码示例可能使用已删除的 API（如 `IDiagramRenderer`、`PuraAgentBlock`、旧的 `UseAgentComponents` 核心 API）。**接入请以上面的当前文档为准。**

> 历史文档中的包名与命名空间为当时口径；2026-11-10 已改名为 `PuraMarkdown.Core` / `PuraMarkdown.Agent` / `PuraMarkdown.Extension`（命名空间随包同步变更），见 [ADR-0028](adr/0028-package-identity-rename.md)。

- `00-需求对齐与设计复审.md`
- `01-requirements.md`
- `02-architecture.md`
- `03-agent-syntax-spec.md`
- `04-plugin-and-rendering.md`
- `05-roadmap-and-risks.md`
- `06-traceability.md`
- `07-IR-虚拟渲染树-多平台-讨论与决策.md`
- `08`–`13`：M0–M4 与 1.0 发布准备
- `14`–`18`：性能分析/修复与虚拟化
- `19`：Git 版本管理
- `20`：Markdown 完整性检查
- `21`：公共 API 与功能扩展建议
- `22`：P1 功能与本地化
- `23`：公共 API 设计评审（当时的 Chat 独立包方案）
- `24`：插件通用接口与 Agent 插件化讨论（当时的 7 包方案）
- `25`：Agent 插件化与公共 API 收敛实现（当时 Agent/Chat 独立成包）

> 用户原始需求：[PuraMarkdown需求方案.md](PuraMarkdown需求方案.md)








