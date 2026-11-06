# ADR-0008：1.0 平台首选 Avalonia 12 桌面，Blazor 作为第二适配器（M4）

- 状态：**被 ADR-0016 取代**（2026-11-05：Avalonia 优先延续，但取消平台抽象、Blazor 移出 1.0 范围）
- 日期：2026-11-05
- 关联：`docs/research/ecosystem.md` §7、需求 OQ-01

## 背景

PuraMarkdown 的核心与平台无关，但适配器投入必须排序。候选：

1. Avalonia 12 桌面；
2. Blazor（Server/WASM/Hybrid/WebView2）；
3. WPF；
4. 先做无 UI 导出（Export）。

## 决策

**开发顺序**：

1. `PuraMarkdown.Export`（HTML/文本/规范化 Markdown）——零平台验证 IR；
2. **`PuraMarkdown.Avalonia`（Avalonia 12）——1.0 的首选与唯一必需适配器**；
3. `PuraMarkdown.Blazor`——M4 的第二适配器，验证可移植性，不作为 1.0 验收前提；
4. WPF：不做（如未来需要，另立 ADR）。

## 理由（依据生态调研）

- Avalonia：单一 .NET 栈、完全离线、无浏览器/CSP 摩擦、AOT 友好、许可干净（MIT）。
- Blazor：组件生态与 A11y 最强，但 JS interop、CSP 放宽、WASM 体积与两套运行时调试成本不符合"桌面优先 + 离线"的当前目标。
- **Avalonia 官方 `Avalonia.Controls.Markdown` 是商业包（Avalonia Pro），不可依赖**；其"AST diff + 增量应用"架构可参考。
- **不依赖 `Markdown.Avalonia` 作为渲染内核**：它是「Markdig → 控件树」，会绕过本项目的 IR 与安全策略；最多参考其排版与主题 token，必要时作为对照实现。默认组件由本项目自建（原生控件 + 主题 token）。

## 后果

- 核心、语法、插件、导出不依赖任何 Avalonia 类型（`Abstractions` 零 UI 依赖）。
- 1.0 验收、性能基线、A11y 验收均在 Avalonia 上完成；Blazor 相关需求（FR-502 的"至少一个参考适配器"）以 Avalonia 满足。
- 交付顺序写入路线图 M1–M4；若后续宿主确定为 Web，仅需把 M4 的 Blazor 适配器前移，不改核心。
- 图表/公式按 ADR-0007 使用 ScottPlot 5 / CSharpMath。

## 备选与回退

若 Avalonia 12 在目标机器上出现无法接受的性能/渲染问题，回退顺序：Avalonia 11 LTS → Blazor WebView2 桌面壳（复用同一 IR，代价是引入 Web 运行时）。
