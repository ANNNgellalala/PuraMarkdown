# 文档索引

## 当前有效（应用接入优先）

| 文档 | 内容 |
| --- | --- |
| [应用集成指南](26-应用集成指南.md) | 包选择、最小接入、Agent/Chat、流式、常见坑 |
| [迁移指南（0.1.0-alpha.1）](27-迁移指南-0.1.0-alpha.1.md) | 从旧版（Agent 在核心/`IDiagramRenderer`）升级 |
| [公共 API 参考](28-公共API参考.md) | 宿主/插件/Chat API 签名与稳定性分级 |
| [Agent 插件化与公共 API 收敛实现](25-Agent插件化与公共API收敛实现.md) | 当前架构变化与验证 |
| [插件通用接口与 Agent 插件化讨论](24-插件通用接口与Agent插件化讨论.md) | 设计决策记录 |
| [公共 API 设计评审](23-公共API设计评审.md) | API 分层、收敛项与开放问题 |
| [ADR 索引](adr/README.md) | 架构决策记录 |

## 历史设计与里程碑（实现可能已过时）

以下文档记录设计/开发过程，代码示例可能使用已删除的 API（如 `IDiagramRenderer`、`PuraAgentBlock`、核心 `UseAgentComponents`）。**接入请以上面的当前文档为准。**

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

> 用户原始需求：[PuraMarkdown需求方案.md](PuraMarkdown需求方案.md)
