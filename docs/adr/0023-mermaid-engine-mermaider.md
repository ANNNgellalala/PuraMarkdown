# ADR-0023：Mermaid 引擎选择 Mermaider（MIT，纯 .NET）；Naiad 因 EULA 排除

- 状态：已接受
- 日期：2026-11-06
- 关联：ADR-0014（图表外置契约）、M3 实现

## 背景

`IDiagramRenderer` 契约已确定，需要为 Mermaid 围栏接入真实引擎。候选：

1. **Mermaider 0.15.1**：MIT，纯 .NET，net10.0，无浏览器/DOM/JS，AOT-ready；输出 SVG（`RenderSvg`）与 ASCII（`RenderAscii`）；自带 Sugiyama 布局；覆盖 24 类图。
2. **Naiad 2.0.1**：net10.0；但 NuGet 许可证为**文件型 EULA（OsmfEula.txt）**，版权声明 "All rights reserved"，不是 MIT/Apache/BSD 类宽松许可。
3. WebView + mermaid.js：需要浏览器运行时/CDN/本地 JS 资源，违反离线与零网络默认。
4. 不接引擎：回退代码块（已有）。

## 决策

1. 采用 **Mermaider 0.15.1**，作为官方可选插件 `PuraMarkdown.Diagrams.Mermaid`。
2. **M3 显示形态用 `MermaidRenderer.RenderAscii`**（等宽文本），避免引入 SVG 渲染依赖；`RenderSvg` 留作后续可选能力。
3. **不采用 Naiad**：EULA/All rights reserved 不符合项目"依赖须 MIT/BSD/Apache"的约束（需求 C-4）。

## 理由

- Mermaider 满足离线、无 JS、AOT、许可干净；渲染一个简单流程图约 95 µs（作者基准），远低于 UI 帧预算。
- ASCII 输出即可验证"真实解析 + 布局 + 渲染"全链路，且不增加 Avalonia SVG 控件/Skia 解码依赖；对 Agent 对话场景可读性足够。
- Naiad 的 EULA 风险不可接受：库的分发会要求下游接受额外许可。
- 保持 `IDiagramRenderer` 契约不变：以后要 SVG 显示，只需替换插件内部实现。

## 后果

- 图表显示为 ASCII（框线字符），视觉表现弱于 SVG；复杂图（C4、序列图）可读性一般。
- Mermaid 解析失败时插件内部捕获并渲染错误卡 + 原始源码；`Complete` 前流式围栏仍按代码块增量显示（M3 现状）。
- 插件依赖 Mermaider（含 Sugiyama、Microsoft.Extensions.ObjectPool），均为宽松许可；需在发布时附带 NOTICE。
- SVG 显示列为后续：可选方案 A（`Avalonia.Svg.Skia` 控件）或 B（`Svg.Skia` 光栅化为 Bitmap）；无论哪种都不改契约。

## 备选

- 若未来需要高保真图：接入 SVG 控件或光栅化；Mermaider 的 SVG 已经过 allowlist 消毒，可安全展示。
- 若需要 Mermaid 之外（PlantUML/D2/Graphviz）：新增独立 `IDiagramRenderer` 插件包，核心不变。
