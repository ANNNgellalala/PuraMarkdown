# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.1.0-alpha.1] - 2026-11-07

首个 alpha 版本：设计定稿（v0.4）+ M0–M4 实现完成。

### Fixed
- 软换行按 CommonMark 渲染为空格，仅硬换行产生换行。
- 流式尾部超限时只在稳定边界强制提交，避免切断未闭合代码围栏/表格。
- 流式稳定块提交前移除旧尾部块，修复代码块重复显示。

### Added

- Avalonia 12.1.5 / .NET 10 只读 Markdown 控件 `PuraMarkdownView`：Markdig GFM 解析、默认块/行内渲染、主题 token、只读与降级路径。
- Agent 交互组件：`pura-agent` JSON 围栏、choice/confirm 内置组件、`IAgentComponentParser`/`IAgentComponentRenderer` 扩展、JSON Schema 校验、交互事件。
- 流式会话：稳定边界 + 尾部窗口增量、后台解析 + UI 合帧、异步与同步两种模式、增量尾部更新、代码高亮后台预计算与流式节流。
- 插件机制：`IPuraPlugin` 校验与失败回退、ColorCode 高亮插件、Mermaid（Mermaider）插件、示例 rating/图表插件。
- 性能与虚拟化：1MB/1000 块块级虚拟化（`EnableBlockVirtualization`）、消息级 `ItemsRepeater` 参考实现、Complete 分帧渲染。
- 测试与工具：`PuraMarkdown.Testing`（控件树 dump/golden/交互探针/流式驱动/插件一致性）、74 个测试、golden 基线。
- 示例与文档：Avalonia Demo、Spike 离屏示例、设计文档、ADR、性能分析/修复记录、虚拟化说明。
- CI：GitHub Actions（restore/build/test/pack），裁剪与 Native AOT 手动验证通过。



