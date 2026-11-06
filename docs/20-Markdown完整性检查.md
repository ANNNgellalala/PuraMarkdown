# Markdown 完整性检查与修复（2026-11-07）

> 方法：代码走查 + 现有测试 + 针对性补测。范围：Markdown 解析/渲染、流式、虚拟化、安全预算、A11y、API/发布。
> 结果：**P0、P1 全部完成；P2 中仅修复"虚拟化交互状态丢失"，其余 P2 暂不处理。当前 85 个测试通过。**

---

## 1. 本轮完成

### P0

| 项 | 实现 |
| --- | --- |
| `MaxBlocks` / `MaxNestingDepth` 未生效 | `DefaultAstSanitizer` 统计块数/嵌套深度；超限块不渲染并给出 `PURA-LIM-005/006` warning |
| `MaxFenceChars` 不约束普通代码围栏 | `CodeBlockRenderer` 截断显示（复制仍复制原文）并给出 `PURA-LIM-002` warning |
| 异步 worker 异常不暴露 | worker 捕获异常后通过 `Warning` 上报 `PURA-STREAM-001`，同时保留 `LastWorkerError` |
| `CompleteAsync` 跨线程限制 | `FinishAsync` 支持从任意线程调用：后台解析 + `Dispatcher` 回到 UI 分帧挂载 |

### P1

| 项 | 实现 |
| --- | --- |
| Autolink 不可点击/不走 URL 策略 | 自动链接复用 Link 渲染：`IUrlPolicy` 放行时生成 `HyperlinkButton`，否则显示灰色文本 |
| 表格列对齐忽略 | 读取 `TableColumnDefinition.Alignment`，对单元格文本应用 Left/Center/Right |
| 表格短行缺列无边框 | 按 `rows × columns` 补齐空单元格 Border |
| A11y live region 不生效 | 新增零透明度 live region `TextBlock`（`AutomationLiveSetting.Polite`），`Announce` 写入文本；测试 dump 跳过该内部节点 |
| 代码复制按钮无自动化名称 | 设置 `AutomationProperties.Name = "复制代码"` |
| `AbortAsync` 同步全量渲染 | 与 Complete 共用后台解析 + 分帧渲染路径 |
| 超大未闭合块尾部无上限 | 新增 `PuraStreamingOptions.MaxTailChars`（默认 1MB）；超限暂停尾部刷新并 warning，Complete 全量校正 |
| 引用定义跨流式窗口不解析 | 会话收集已解析的 `LinkReferenceDefinition`，解析后续窗口时自动前置定义并在渲染时过滤定义组 |
| 虚拟化交互状态丢失 | 见下节 |

### P2（仅完成一项）

**虚拟化回收后交互组件状态丢失**：

- 原因：`ItemsRepeater` 回收视口外块控件；原状态保存在 `PuraChoiceControl` 实例字段（选中集合/RadioButton 勾选）中，控件销毁即丢失，重新实例化得到默认状态。
- 修复：
  1. `PuraMarkdownView` 按 `ComponentId` 记录最近一次 `InteractionOccurred` 状态；
  2. `RenderContext.ComponentStates` 传给组件渲染器；
  3. `AgentComponentRenderer` 渲染后若控件实现 `IPuraInteractionStateReceiver`，应用历史状态；
  4. 内置 choice 与示例 rating 控件实现该接口，恢复选中项/禁用已提交按钮。
- 效果：滚动回收、主题重渲染、Complete 全量重建后，组件状态仍然保留；不依赖保留控件实例。

---

## 2. 仍不处理（P2）

| 项 | 说明 |
| --- | --- |
| 图片字节/超时预算 | 仍由宿主 `IImageLoader` 负责 |
| 无 LICENSE 文件 | 包使用 MIT 表达式，仓库缺 LICENSE 文本 |
| 公开 XML 注释质量 | 部分成员仍为脚本生成的简短摘要 |
| CI 每次 push 都 pack | 可后续拆 ci / release 工作流 |
| CommonMark 规范子集 | 目前为精选测试 + golden，未引入完整 spec |
| 脚注/定义列表 | 未启用（按需求决定） |

---

## 3. Markdown 特性覆盖

| 能力 | 状态 |
| --- | --- |
| CommonMark 段落/标题/强调/链接/图片/列表/引用/代码/分隔线 | ✅ |
| 硬/软换行 | ✅（软换行按空格） |
| GFM 表格 | ✅（列对齐 + 缺列补齐） |
| GFM 任务列表 | ✅（只读 CheckBox） |
| GFM 删除线/自动链接 | ✅（自动链接可点击并走 URL 策略） |
| 行内代码/围栏代码/缩进代码 | ✅（围栏预算生效） |
| 引用式链接 | ✅（流式跨窗口支持；定义在当前窗口之后仍受 append-only 限制） |
| HTML | ❌ 设计上禁用（ADR-0009） |
| 脚注/定义列表 | ❌ 未启用 |
| 数学公式 | ❌ 插件能力（未提供官方插件） |
| Mermaid | ✅ 官方插件（Mermaid ASCII） |

---

## 4. 新增测试

本轮新增 12 个测试，总数 **85**：

- 预算：`MaxBlocks_limits_rendered_blocks_and_warns`、`MaxNestingDepth_blocks_deeply_nested_content`、`MaxFenceChars_truncates_code_block`
- 渲染：`Autolink_is_clickable_and_respects_url_policy`、`Table_applies_column_alignment_and_fills_missing_cells`、`Announce_updates_live_region`
- 流式：`Abort_renders_current_content_and_announces`、`Reference_definition_from_committed_window_resolves_in_tail`、`Oversized_tail_pauses_refresh_and_completes`、`Complete_can_be_called_from_background_thread`
- 换行/围栏：`Soft_line_break_renders_as_space`、`Hard_line_break_renders_as_line_break`、`Long_unclosed_fence_is_not_split_by_forced_commit`
- 虚拟化状态：`Virtualized_component_state_is_restored_after_recreation`

```
dotnet build PuraMarkdown.slnx        → 0 警告 / 0 错误
dotnet test tests/PuraMarkdown.Tests  → 85 通过 / 0 失败
```

---

## 5. 建议的后续

1. P2 收尾：LICENSE、CI 拆分（tag 才 pack）、CommonMark spec 子集；
2. 引用式链接"定义后置"的 append-only 场景可提供 warning 或延迟重渲染；
3. 图片加载预算、屏幕阅读器手工走查。
