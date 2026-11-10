# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- **Emoji/Smiley**：`PuraPipelineBuilder.UseEmoji(bool enableSmileys = false)` / `PuraSyntaxBuilder.UseEmoji(...)`；`EmojiInline` 渲染为 Unicode emoji，smiley 默认关闭（避免 `->`/`<=` 等文本被替换）；节点矩阵与文档更新。
- **数学公式（核心语法 + TeX 回退）**：`PuraPipelineBuilder.UseMathematics()` / `PuraSyntaxBuilder.UseMathematics()`；核心不引用排版引擎，默认显示 TeX 源码（块级带复制按钮）；流式尾部使用文本回退，扫描器识别 `$$` 块（ADR-0026）。
- **行内插件分发修复**：`DefaultInlineRenderer` 现在会在 `MathInline`/未知行内节点上询问 `IPuraInlineRenderer`，插件可接管（此前该契约从未被调用）。
- **CSharpMath 示例插件**：`samples/PuraMarkdown.Plugins.Samples/CSharpMathMathPlugin.cs` 展示行内/块级 `MathView` 排版；Demo/Spike 引用它。`CSharpMath.Avalonia` 只存在于示例项目，**核心包不依赖**。
- 新增 `EmojiMathTests`（10 个测试）：短代码/smiley 开关、代码块内不转换、强调内不粗体、数学节点/矩阵、默认 TeX 回退、示例插件排版、货币误判、`$$` 块流式等价、尾部文本回退。

### Changed

- **打包整合：7 个 NuGet 包 → 3 个**（ADR-0027）。`PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing` 三个包并入核心包 `PuraMarkdown`；`PuraMarkdown.Highlighting.ColorCode` 与 `PuraMarkdown.Diagrams.Mermaid` 合并为新的可选插件包 `PuraMarkdown.Plugins`（含 `ColorCodeHighlighter`/`ColorCodePlugin` 与 `MermaidDiagramRenderer`/`MermaidPlugin`）。**C# 命名空间未改名**：`PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing`、`PuraMarkdown.Highlighting.ColorCode`、`PuraMarkdown.Diagrams.Mermaid` 均保留在新程序集中，宿主源码无需改动，只改 `PackageReference`。包版本仍为 `0.1.0-alpha.1`（这些包只做过本地 `dotnet pack` 验证，**未发布到 nuget.org**）。ADR-0014「核心零引擎依赖」规则保持有效，全部引擎依赖（ColorCode.Core、Mermaider）集中在 `PuraMarkdown.Plugins`。
- `MarkdigCompatibility` 增加 `EmojiInline`、`MathInline`、`MathBlock`；`StreamingBlockScanner` 增加 `trackMathBlocks` 重载。
- 数学排版引擎不再进核心：`PuraMarkdown.nupkg` 依赖仍为 Syntax/Avalonia/ItemsRepeater/Markdig（另含内置 `pura-agent` 组件所需的 JsonSchema.Net）；需要排版的宿主自行注册渲染器或使用示例插件（ADR-0026）。
- 测试总数 119 → 129；README/文档 27/28/29/35/36 与 ADR 索引同步更新。

### Known limitations

- 数学只支持 Markdig 的 `$...$` / `$$...$$`；`\(...\)` / `\[...\]` 需宿主或 Agent 侧先归一化。
- 官方不发布数学引擎包；示例项目使用 `CSharpMath.Avalonia` 1.0.0-pre.2（prerelease），Trim/AOT 冒烟在示例插件路径上验证。

## [0.1.0-alpha.1] - 2026-11-07

首个 alpha 版本：设计定稿（v0.4）+ M0–M4 + 公共 API 收敛。

### Added

- Avalonia 12.1.5 / .NET 10 只读 Markdown 控件 `PuraMarkdownView`：Markdig GFM 解析、默认块/行内渲染、主题 token、只读与降级路径。
- 流式：稳定边界 + 尾部窗口增量、后台解析 + UI 合帧、`AppendAsync`/`FlushAsync`/`Completed`/`Aborted`、取消、代码高亮后台预计算与节流。
- 宿主功能：文本选择/复制/纯文本、大纲与锚点滚动、链接/图片事件、系统主题实时切换。
- 本地化：`IPuraLocalizer` / `ChinesePuraLocalizer`，默认中文，可替换 UI 文案。
- Chat 会话能力：消息时间戳/版本/元数据/错误、历史增删改、自动滚动策略、流式同消息更新、中断/失败状态、快照 JSON。
- Agent 能力：交互状态持久化与 JSON 序列化、组件目录 `ComponentTypes`、组合式组件注册与重复校验。
- 插件契约：`IFenceRenderer`（取代 `IDiagramRenderer`）、`UseInlineRenderer`、`UseSanitizer`、插件 fail-fast/回退链。
- 性能与虚拟化：块级虚拟化（`EnableBlockVirtualization`）、`PuraMarkdown.Chat` 消息级虚拟化、Complete 分帧渲染。
- 测试与工具：`PuraMarkdown.Testing`、119 个测试、golden 基线、性能诊断模式。
- 示例与文档：Avalonia Demo、Spike、设计文档、ADR、性能/虚拟化/API 文档。
- CI：GitHub Actions（restore/build/test/pack），裁剪与 Native AOT 手动验证通过。

### Changed

- **Agent 语法移出核心**：新增 `PuraMarkdown.Agent`（`AgentPlugin`/`AgentFenceRenderer`），核心不再依赖 JsonSchema.Net；支持 `UseAgentComponent` 组合式注册。
- **Chat 独立**：新增 `PuraMarkdown.Chat`（`PuraChatView`/`PuraChatMessage`）。
- **语法/渲染分离（B 阶段）**：新增 `PuraMarkdown.Syntax`（`PuraDocument`/诊断/大纲/扫描）；`PuraMarkdown` 消费语法层，新增 `PuraDocumentView`/`IPuraNodeRenderer`。所有包接入 PublicAPI 分析器；Avalonia Trim/AOT 与性能基线已复跑。
- **B 阶段 P0 修复**：sanitizer/limits/url policy 下沉 Syntax，`PuraDocument.Diagnostics` 可消费；View 复用 `PuraDocument.Sanitized`，流式解析切换 Syntax，支持顶层块级增量（任意编辑定位受影响块，前后扩一块复用其余 AST；引用定义/异常回退全量）。
- **Markdig 节点矩阵**：`MarkdigCompatibility` 固化支持节点子集、版本契约与升级检查；文档见 `docs/35-Markdig支持节点子集与兼容矩阵.md`。
- **Markdown 扩展**：脚注（含引用/回链）、定义列表、GitHub Alerts；EmphasisExtras 的上标/下标/插入/高亮语义修正。
- Mermaid 插件迁移到 `IFenceRenderer`。
- 版本统一由 `Directory.Build.props` 管理，标记 `v0.1.0-alpha.1`。

### Breaking

- 移除 `UseLimits`/`PuraRenderEnvironment.Limits`、`PuraMarkdownOptions.Logger`、`PuraStreamingOptions.EnableBlockReuse`、`IPuraPlatformServices`/`DefaultPlatformServices`。
- `IDiagramRenderer`/`UseDiagramRenderer` 由 `IFenceRenderer`/`UseFenceRenderer` 取代。
- `PuraAgentBlock`/`PuraAgentBlockParser`/`PuraAgentExtension` 与核心 Agent 组件 API 移除；改用 `PuraMarkdown.Agent`。
- `PuraRendererRegistry.Registrations` 改为 `Renderers`。

### Fixed

- 安全预算：`MaxBlocks`/`MaxNestingDepth`/`MaxFenceChars` 生效并给出 warning。
- 流式：worker 异常通过 Warning 上报；`MaxTailChars` 限制超大未闭合块；引用定义跨流式窗口解析；Complete/Abort 支持后台线程调用。
- 渲染：自动链接可点击并走 URL 策略；表格列对齐与缺列补齐；软/硬换行修正；A11y live region 与复制按钮名称。
- 虚拟化：交互组件状态按 ComponentId 持久化，回收/重建后恢复。
- 流式尾部超限时只在稳定边界强制提交，避免切断未闭合代码围栏/表格。
- 流式稳定块提交前移除旧尾部块，修复代码块重复显示。

















