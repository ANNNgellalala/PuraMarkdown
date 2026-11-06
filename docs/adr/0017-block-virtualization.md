# ADR-0017：块级虚拟化采用可选 ItemsRepeater 模式

- 状态：已接受（2026-11-07）
- 关联：ADR-0023、[多轮长对话性能验证](../16-多轮长对话性能验证.md)、[块级虚拟化实现](../18-块级虚拟化实现.md)

## 背景

单条超长消息（1MB / 1000 块）一次性实例化全部块控件时：

- 打开耗时约 2.6s；
- 控件内存约 70MB；
- 布局/滚动成本随块数增长。

此前默认 `StackPanel` 全量挂载，对聊天里的长代码/长表格不友好。

## 决策

1. 在 `PuraMarkdownOptions` 新增 **`EnableBlockVirtualization`**（默认 `false`，保持既有行为）。
2. 启用时，`PuraMarkdownView` 内部使用 `ScrollViewer + ItemsRepeater + StackLayout`，只实例化视口内的块；控件自带滚动容器。
3. 块元数据（`Blocks`）仍完整保留；`BlockRecord.View` 在虚拟模式下为 `null`，控件按需由 `ItemTemplate` 创建。
4. 流式路径（稳定块/尾部窗口）同步维护虚拟集合；尾部更新替换对应条目。
5. 新增 `ScrollToEnd()` 便于宿主在流式追加后跟随到底部。

## 理由

- `ItemsRepeater` 是 Avalonia 官方虚拟化容器，支持变高块、回收；无需自研测量/复用算法。
- 默认关闭，不影响现有宿主、golden、截图与固定块布局。
- 与消息级虚拟化（宿主使用 `ItemsRepeater` 承载多条 `PuraMarkdownView`）互补。

## 后果

- 核心包新增依赖 `Avalonia.Controls.ItemsRepeater`（Avalonia 官方包，12.0.0）。
- 启用后宿主**不要**再在外层套 `ScrollViewer`（双滚动）。
- 视口外的交互组件控件会被回收，其本地选择状态不保留；交互结果仍通过事件上报，宿主应以事件为准。
- 不自动滚动；宿主可在流式追加后调用 `ScrollToEnd()`。
- 默认仍为全量 `StackPanel`，1.0 文档需明确两种模式的选择建议。

## 备选

- 自研虚拟化 Panel：可控但维护成本高，未采用。
- 固定块高：实现简单但 Markdown 块高变化大，显示/滚动体验差，未采用。
- 始终虚拟化：会改变现有布局语义（块间距、外层滚动、截图），不采用。
