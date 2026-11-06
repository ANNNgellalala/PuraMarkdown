# Markdown 完整性检查（2026-11-07）

> 方法：代码走查 + 现有测试 + 针对性补测。范围：Markdown 解析/渲染、流式、虚拟化、安全预算、A11y、API/发布。
> 本轮已修复 2 个确认问题；其余按优先级列出，暂未改动。

---

## 1. 本轮修复

### 1.1 软换行被错误渲染成硬换行（P1，已修复）

- 位置：`DefaultInlineRenderer` 的 `LineBreakInline` 分支。
- 现象：`line1\nline2` 在 CommonMark 中应渲染为"line1 line2"（软换行=空格），实际每个换行都变成 `<br/>`。
- 修复：按 `LineBreakInline.IsHard` 区分；软换行插入空格，硬换行（行尾两空格/反斜杠）才插入 `LineBreak`。
- 测试：`Soft_line_break_renders_as_space`、`Hard_line_break_renders_as_line_break`。

### 1.2 尾部窗口强制提交可能切断围栏/表格（P0，已修复）

- 位置：`PuraStreamingSession.BuildUpdate` 的 `TailWindowChars` 超限逻辑。
- 现象：尾部超过 16KB 时按最近 `\n\n` 强制提交；若该位置在未闭合代码围栏、表格或列表中，会把块从中间切断，产生重复/错乱块（Complete 才恢复）。
- 修复：只有当前缀本身通过 `StreamingBlockScanner.FindStableBoundary` 判定为稳定边界时才强制提交；否则继续保留尾部（宁可尾部变大，也不破坏结构）。
- 测试：`Long_unclosed_fence_is_not_split_by_forced_commit`（>16KB 未闭合围栏只产生 1 个块）。

---

## 2. 仍待完善（按优先级）

### P0

| 问题 | 说明 | 建议 |
| --- | --- | --- |
| `MaxBlocks` / `MaxNestingDepth` 未生效 | `PuraLimits` 中定义但渲染/清洗路径未使用；超长块数或极深嵌套无法按预算降级 | 在 sanitizer/渲染入口统计块数与深度，超限截断/降级并给 warning |
| `MaxFenceChars` 只作用于 `pura-agent` | 普通代码围栏不检查大小，超长代码块可绕过预算 | `CodeBlockRenderer`/sanitizer 对代码围栏应用上限（截断 + warning） |
| 异步 worker 异常不向宿主暴露 | 后台解析异常只存 `LastWorkerError`，界面无 warning，表现为"不更新" | worker 捕获后通过 `Warning` 事件上报 `PURA-STREAM-001` |
| `CompleteAsync` 依赖 UI 线程 | 若宿主从后台线程调用，`RenderPreparedAsync` 会跨线程访问控件 | 文档明确要求，或内部 `Dispatcher.InvokeAsync` 兜底 |

### P1

| 问题 | 说明 | 建议 |
| --- | --- | --- |
| Autolink 不可点击/不走 URL 策略 | `AutolinkInline` 仅渲染为带下划线文本 | 复用 Link 分支生成 `HyperlinkButton`，并走 `IUrlPolicy`/`LinkHandler` |
| 表格列对齐被忽略 | 未读取 Markdig `TableColumnDefinition.Alignment` | 按列设置 `TextAlignment`/单元格 HorizontalAlignment |
| 表格缺列无边框 | 短行时未给缺失单元格占位 Border | 补齐空单元格 Border，视觉一致 |
| A11y 播报没有实际 live region | `LiveSetting` 设在视图上，`Announce` 仅触发事件，读屏未必播报 | 增加不可见/零尺寸的 live region TextBlock 并更新文本 |
| 代码复制按钮缺自动化名称 | 只有 `Content="复制"` | `AutomationProperties.SetName(copyButton, "复制代码")` |
| `AbortAsync` 同步全量渲染 | 中断时仍在 UI 线程全量 `RenderText`，长消息会卡 | 复用 `PreparedDocument` + 分帧路径 |
| 超大未闭合块尾部无上限 | 安全修复后极端情况尾部可持续增长，后台解析 O(n²) | 追加"超大尾部纯文本占位/降级"策略 |
| 引用链接定义跨窗口 | 流式期间 `[id]: url` 定义在已提交窗口时可能延迟解析 | 文档已标注；可在 Complete 校正前提供 warning |

### P2

| 问题 | 说明 |
| --- | --- |
| 图片无字节/超时预算 | 由宿主 `IImageLoader` 负责；建议文档明确契约 |
| 虚拟化交互状态 | 视口外交互组件控件回收，本地选择状态丢失（已文档化） |
| 无脚注/定义列表 | GFM 脚注、定义列表未启用；按需求决定是否加入 |
| 无 LICENSE 文件 | 包使用 MIT 表达式，但仓库缺 LICENSE 文本 |
| 公开 XML 注释质量 | 大量成员为脚本生成的简短摘要，需逐步人工完善 |
| CI 每次 push 都 pack | 可拆成 ci（build/test）与 release（tag 触发 pack） |
| 无 CommonMark 规范用例集 | 目前为精选测试，建议引入 spec 子集做 golden |

---

## 3. Markdown 特性覆盖现状

| 能力 | 状态 |
| --- | --- |
| CommonMark 段落/标题/强调/链接/图片/列表/引用/代码/分隔线 | ✅ |
| 硬/软换行 | ✅（本轮修复软换行） |
| GFM 表格 | ✅（列对齐待补） |
| GFM 任务列表 | ✅（只读 CheckBox） |
| GFM 删除线/自动链接 | ✅（自动链接不可点击，待补） |
| 行内代码/围栏代码/缩进代码 | ✅（代码围栏预算待补） |
| 引用式链接 | ✅（流式跨窗口有限制） |
| HTML | ❌ 设计上禁用（ADR-0009），按字面文本显示 |
| 脚注/定义列表 | ❌ 未启用 |
| 数学公式 | ❌ 插件能力（未提供官方插件） |
| Mermaid | ✅ 官方插件（Mermaid ASCII） |

---

## 4. 测试与验证

本轮新增 3 个测试，总数 **74**：

- `Soft_line_break_renders_as_space`
- `Hard_line_break_renders_as_line_break`
- `Long_unclosed_fence_is_not_split_by_forced_commit`

```
dotnet build PuraMarkdown.slnx        → 0 警告 / 0 错误
dotnet test tests/PuraMarkdown.Tests  → 74 通过 / 0 失败
```

---

## 5. 建议的后续顺序

1. P0：预算（MaxBlocks/深度/代码围栏）+ worker 异常上报；
2. P1：autolink、表格对齐、A11y live region；
3. P2：LICENSE、CommonMark 子集、CI 拆分 pack。
