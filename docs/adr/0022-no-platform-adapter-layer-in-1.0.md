# ADR-0022：跨平台由 Avalonia 承担，1.0 不建渲染适配层

- 状态：**已接受**（2026-11-05 用户确认：选项 A；跨平台由 Avalonia 承担，不建适配层）
- 日期：2026-11-05
- 关联：[讨论文档](../07-IR-虚拟渲染树-多平台-讨论与决策.md)、ADR-0016、ADR-0011

## 背景

v0.2 通过"平台无关核心 + 适配器"支持多平台。v0.3 决定核心直接依赖 Avalonia。用户确认跨平台交由 Avalonia 承担。需明确是否仍要一层渲染适配抽象。

## 决策

1.0 **不做渲染适配层**。跨平台 = Avalonia 的 Windows/macOS/Linux 支持；平台差异只通过少量**服务接口**隔离（剪贴板、字体、无障碍播报等），不抽象控件。

## 理由

- Avalonia 已统一窗口/控件/布局/输入/渲染/DPI/基础无障碍；
- 1.0 的跨平台需求仅是桌面三平台，无需第二渲染后端；
- 适配层会让插件契约在"平台无关类型"与"Avalonia 控件"之间二选一：要么失去 Avalonia 能力，要么适配层形同虚设；
- 成本 3–4 周，且每加平台需重写默认渲染组（v0.2 老路）。

## 平台差异的隔离方式（薄接口，不是适配层）

| 能力 | 接口 | 默认实现 |
| --- | --- | --- |
| 剪贴板 | `IPuraClipboard` | Avalonia `TopLevel.Clipboard` |
| UI 线程 | `IDispatcher` | `Dispatcher.UIThread`（已在 `RenderContext`） |
| 图片加载 | `IImageLoader` | 不联网占位 |
| 链接打开 | `ILinkHandler` | 不自动打开 |
| 无障碍播报 | `IAccessibilityAnnouncer`（可选） | Avalonia AutomationPeer |

## 后果

- 平台相关缺陷（输入法、字体回退、读屏、剪贴板差异）需在 Windows/macOS/Linux 分别验证；CI 以 Windows + Linux headless 为主，macOS 冒烟。
- 不支持 Blazor/原生 MAUI/无 Avalonia 宿主；如需，触发 ADR-0011 备选方案（抽平台无关渲染描述），预计 3–4 周。

## 触发条件

1. 需要 Blazor/Web 或原生 MAUI 渲染；
2. 需要无 UI 的渲染服务；
3. Avalonia 在目标平台出现不可接受的限制（输入法/无障碍/性能）。

## 备选

现在建适配层（+3–4 周）：1.0 无第二平台，收益为零；仅在明确 2.0 多后端计划时考虑。
