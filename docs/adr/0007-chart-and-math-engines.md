# ADR-0007：图表与公式——核心只定义数据契约，Avalonia 适配 ScottPlot 5 + CSharpMath

- 状态：**被 ADR-0014 取代**（2026-11-05：图表/公式改为外置插件契约，核心不定义 ChartSpec）
- 日期：2026-11-05
- 关联：ADR-0008（平台）、语法规范 §7.7、`docs/research/ecosystem.md` §3–§4

## 背景

Agent 输出需要图表与数学公式。候选路线：

1. 核心直接集成某个图表库（把库类型写进 IR/契约）；
2. 核心定义**引擎无关的数据契约**（`ChartSpec`、TeX 节点），适配器负责翻译到具体引擎；
3. 只输出占位/图片 URL，渲染交给宿主。

## 决策

采用 **方案 2**：

- 核心（`Abstractions`/`Core`）只定义：
  - `ChartSpec`：`chartType`（bar/line/area/pie/scatter，M3 起 radar）、`categories`、`series(name/data)`、轴标题与范围、图例、堆叠、`options.x-<engine>` 透传位；
  - 公式节点：TeX 源码（`$...$` / `$$...$$`），不定义排版。
- **Avalonia 适配器**：图表 → **ScottPlot 5**（纯 .NET/Skia，MIT，离线）；公式 → **CSharpMath**（纯 .NET，MIT）。两者缺少的复杂能力降级：图表 → 数据表；公式 → 等宽 TeX。
- **Blazor 适配器（M4）**：图表 → ECharts（本地打包，Apache-2.0）；公式 → KaTeX。核心契约不变。
- 导出渲染器不渲染图表，输出数据表 + 标题/单位。

## 理由

- 图表库类型进入 IR 会让核心与某个 UI 生态绑定，换适配器即改核心（违反 ADR-0001/0003 的可移植目标）。
- `ChartSpec` 的"最小公共子集"足以覆盖 Agent 场景（趋势/对比/占比/分布），且对 LLM 生成友好（比直出 ECharts option 更稳、token 更少）。
- ScottPlot 离线、无 JS、AOT 较友好；CSharpMath 无 JS 依赖，符合桌面壳的 CSP/离线约束。

## 后果

- 引擎特有外观/交互只能通过 `options.x-<engine>` 或适配器侧扩展实现；核心测试不依赖引擎。
- `ChartSpec` 的演进需保持向后兼容（新增可选字段；未知字段忽略 + 诊断）。
- Blazor 侧图表能力在 M4 前不可用（降级数据表），这是可接受的交付顺序。

## 备选

- LiveCharts2（动画/主题更好，依赖 Skia/GPU，裁剪需实测）：若 ScottPlot 交互体验不足可替换，仅改适配器。
- 自绘 SVG：适合导出与无依赖场景，但交互能力弱，暂不做。
