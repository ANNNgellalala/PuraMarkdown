# PuraMarkdown 生态选型调研：.NET 渲染栈、图表与 Agent UI 协议

> 范围：为 PuraMarkdown（Markdig 解析 → 渲染到 UI 的只读 Agent 对话控件）提供选型依据。
> 方法：2026-11-03～04 经 GitHub API / NuGet / 官方文档核对版本、许可证与最近发布；未确证者标「待验证」。
>
> **决策后注记（2026-11-05，v0.3）**：本文是调研记录，不是最终方案。v0.3 已按用户需求方案重构：**渲染直接消费 Markdig AST、产出 Avalonia 控件，无 IR（ADR-0011）**；**1.0 只做 Avalonia 12 原生控件（ADR-0016）**；**图表/公式/高亮外置为插件契约（ADR-0014）**；**目标框架 net10.0（ADR-0013）**。因此本文中"自建 IR""Avalonia Pro 控件架构范本""ScottPlot/CSharpMath 进核心"等建议仅作事实参考，落地方案以 ADR 为准。

## 0. 结论摘要

1. **不要用现成 Markdown 控件当内核**：唯一原生支持「流式增量+可定制 Markdig 管道」的 [Avalonia 官方 Markdown 控件](https://docs.avaloniaui.net/controls/documents/markdown)属 **Avalonia Pro 商业包**（需 license key），依赖它会把订阅义务传染给每个宿主。
2. **Markdig 已到 1.x**（[1.5.1，2026-10-28，BSD-2](https://github.com/xoofx/markdig/releases/tag/1.5.1)），社区渲染器多停在旧版（Markdig.Wpf 停于 2021，绑 0.22）；自建渲染层是唯一能跟进 1.x 的路线。
3. **推荐主线**：核心库只出 IR + 组件描述符；首个适配器 **Avalonia 12**（Markdown.Avalonia 排版、ScottPlot 图表、CSharpMath 公式、AvaloniaEdit 高亮），**Blazor 第二优先**（MudBlazor+ECharts+KaTeX，上限最高但需 JS interop）。
4. **Agent UI 协议**：[A2UI](https://github.com/google/A2UI)/[json-render](https://github.com/vercel-labs/json-render) 的「catalog+声明式组件树」与我们最同构，可借 schema 校验与增量语义；[AG-UI 1.0](https://docs.ag-ui.com/spec/1.0) 作事件流契约；MCP Apps/OpenAI Apps SDK 的 HTML-iframe 路线过重。
5. **三条硬约束**：离线（禁 CDN）、AOT/裁剪、许可干净（MIT/BSD/Apache 可闭源分发）。

## 1. .NET Markdown 渲染器横向对比

| 名称 | 平台 | 许可证 | 能力 | 局限 | 结论 |
|---|---|---|---|---|---|
| [Markdig](https://github.com/xoofx/markdig) | netstandard2.0/net8+ | BSD-2 | CommonMark 合规、扩展极多、可自定义 Block/Inline 与 `ObjectRenderer` | 只出 HTML/AST，无 UI 控件 | ✅ 必选解析内核 |
| [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) | WPF | MIT | AST→`FlowDocument`，可改写 renderer | [v0.5.0.1（2021-05-29）](https://github.com/Kryptos-FR/markdig.wpf/releases) 后停更，绑 Markdig 0.22，无流式 | ❌ 仅作参考 |
| [Markdown.Avalonia](https://github.com/whistyun/Markdown.Avalonia) | Avalonia 11/12 | MIT | [v12.0.2（2026-10-17）](https://github.com/whistyun/Markdown.Avalonia/releases/tag/v12.0.2)：`MarkdownScrollViewer`、Svg/Html/SyntaxHigh 插件、文本选择、AOT | 控件级 API，扩展靠插件而非自定义节点；无流式追加 | ✅ Avalonia 最佳起步 |
| [Avalonia.Controls.Markdown](https://docs.avaloniaui.net/controls/documents/markdown) | Avalonia 12 | 商业订阅 | 架构最贴近目标：AST→`DocumentSnapshot`→`FlowDocument`；`BeginStreaming()`、后台 AST diff 只应用变化块、`AutoScrollToEnd`、可重写 `ConfigurePipeline` | 需 license key，下游需订阅 | ⚠️ 架构范本，不可依赖 |
| [CommunityToolkit MarkdownTextBlock](https://www.nuget.org/packages/CommunityToolkit.WinUI.UI.Controls.Markdown) | UWP/WinUI 2 | MIT | `MarkdownTextBlock`、可插拔 `IMarkdownRenderer` | 停在 7.1.2；WinUI 3/8.x 无等价控件 | ❌ WinUI 3 无现成方案 |
| MAUI | MAUI | — | 无官方控件 | 需自建或 WebView 嵌 Blazor | ⚠️ 成本高 |
| Blazor 生态（MudBlazor/Radzen/BlazorMarkdown） | Blazor | MIT | 通行做法：Markdig→HTML→`MarkupString`+sanitizer；MudBlazor 无内置 Markdown | 自定义组件需自写 Razor 映射 | ⚠️ 走「IR→Razor」自建 |

**1.1 三个决定性问题**

- **自定义节点**：Markdig 全支持；Markdown.Avalonia 只开放「插件 IF」；Markdig.Wpf/Xaml、MarkdownTextBlock 几乎不可扩展；Avalonia Pro 只开放管道配置。MdXaml 为其上游；`Markdig.Wpf.Xaml`、`LiveMarkdown.Avalonia` 未检索到独立维护，Havit.Blazor 亦无 Markdown 组件（待验证）。
- **流式追加**：只有 Avalonia Pro 开箱支持；其余为全量重渲染 → 流式必须在 PuraMarkdown 的 block 层自建。
- **可替换渲染器**：只有 Markdig 真正可替换，其余把「AST→控件树」写死在内部，这正是「IR+适配器」的依据。

## 2. UI 组件库候选（按平台）

| 名称 | 平台 | 许可证 | 关键能力 | 局限 | 结论 |
|---|---|---|---|---|---|
| [MudBlazor](https://github.com/MudBlazor/MudBlazor) | Blazor | MIT | [v9.12.0（2026-10-30）](https://github.com/MudBlazor/MudBlazor/releases/tag/v9.12.0)；表单/Radio/Checkbox/Card/ExpansionPanel/Progress 齐全，`MudDataGrid` 虚拟化+分组+服务端数据；近版重性能（渲染 -43%）与 A11y | Material 强绑定、CSS 体积大、无 Markdown | ✅ Blazor 首选 |
| [Radzen.Blazor](https://github.com/radzenhq/radzen-blazor) | Blazor | MIT | [v12.1.10（2026-11-02）](https://github.com/radzenhq/radzen-blazor/releases/tag/v12.1.10)；企业级 `RadzenDataGrid`、内置 `RadzenChart`；密集修 A11y | 视觉偏企业；商业支持付费 | ✅ Blazor 备选 |
| [Fluent UI Blazor](https://github.com/microsoft/fluentui-blazor) / [Ant Design Blazor](https://github.com/ant-design-blazor/ant-design-blazor) | Blazor | MIT | 前者微软官方/Fluent 2/DataGrid 虚拟化；后者组件丰富、中文文档好 | 前者覆盖与文档弱于 MudBlazor 且版本有 break；后者 A11y 一般 | ⚠️ 按设计体系选 |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia)+[Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia)/[Ursa](https://github.com/irihitech/Ursa.Avalonia) | Avalonia 11/12 | MIT | 跨三平台、主题质量高、原生虚拟化面板 | 单选/多选/卡片需自建或拼装；UIA 支持不完整 | ✅ 桌面首选 |
| [HandyControl](https://github.com/HandyOrg/HandyControl)/[MahApps](https://github.com/MahApps/MahApps.Metro)/[WPF UI](https://github.com/lepoco/wpfui) | WPF | MIT | 成熟主题、`VirtualizingStackPanel` 稳定 | 平台前景弱、无 NativeAOT 路径 | ⚠️ 仅存量 WPF |

**要点**：虚拟化是长对话生死线（Blazor/WPF 开箱可用，Avalonia 需自研块级虚拟化）；主题由 PuraMarkdown 暴露统一 `ThemeTokens` 供适配器映射。

## 3. 图表方案

| 方案 | 技术栈 | 许可证 | 离线 | 性能与交互 | AOT/裁剪 | 结论 |
|---|---|---|---|---|---|---|
| [ECharts](https://echarts.apache.org) | JS canvas | Apache-2.0 | ✅ 本地打包 | 交互最强（缩放/tooltip/联动/采样） | JS 不受裁剪 | ✅ Blazor 首选 |
| [Chart.js](https://www.chartjs.org)（`ChartJs.Blazor`） | JS canvas | MIT | ✅ | 中等；⚠️ 包装库活跃度低 | 同上 | ⚠️ 轻量场景 |
| [ScottPlot](https://scottplot.net) | 纯 .NET+Skia | MIT | ✅ | [5.1.59（2026-06-22）](https://github.com/ScottPlot/ScottPlot/releases/tag/5.1.59)：百万点流畅、支持 Avalonia 12；动画弱 | 友好（SkiaSharp AOT 待实测） | ✅ 桌面首选 |
| [LiveCharts2](https://github.com/Live-Charts/LiveCharts2) | 纯 .NET | MIT | ✅ | [2.0.4（2026-05-16）](https://github.com/Live-Charts/LiveCharts2/releases/tag/2.0.4) 正式版；动画/主题好 | 依赖 Skia/GPU，裁剪需实测 | ✅ 桌面交互备选 |
| [Vega-Lite](https://vega.github.io/vega-lite/) | JS 语法式 | BSD-3 | ✅ 需打包 | 声明式极佳（LLM 友好） | 需浏览器/WebView | ⚠️ 图表 DSL 路线 |

（另：[OxyPlot](https://github.com/oxyplot/oxyplot) 纯 .NET/MIT，稳定但迭代慢，仅存量兼容用。）

**首选/备选**：桌面选 **ScottPlot 5**（纯 .NET、离线、MIT），需动画/联动用 LiveCharts2；Blazor 选 **ECharts**（强于 Chart.js，代价是自封装 interop；`Blazor.ECharts`/`ECharts.Net` 等包装库生态小）。图表由 LLM 输出 PuraMarkdown 图表声明、适配器再翻译，比直接吐 option 更稳。

## 4. 公式、代码高亮与 Mermaid

| 类别 | 候选 | 许可证 | 形态 | 结论 |
|---|---|---|---|---|
| 公式 | [KaTeX](https://katex.org) [0.19.1](https://www.npmjs.com/package/katex) | MIT | JS，CSS+字体可离线 | ✅ Web/Blazor 首选：同步渲染、体积小 |
| 公式 | [MathJax](https://www.mathjax.org) v4（版本待验证） | Apache-2.0 | JS | ⚠️ 复杂 LaTeX/无障碍更强，但包大、默认异步 |
| 公式 | [CSharpMath](https://www.nuget.org/packages/CSharpMath.Avalonia)/[WpfMath](https://github.com/ForNeVeR/wpf-math) | MIT | 纯 .NET | ✅ 桌面首选；⚠️ 复杂宏/矩阵/中文混排覆盖有限 |
| 高亮 | [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)+[TextMateSharp](https://github.com/danipen/TextMateSharp) / [ColorCode-Universal](https://github.com/WilliamABradley/ColorCode-Universal) | MIT | 纯 .NET | ✅ 桌面首选（Markdown.Avalonia 的 SyntaxHigh 即此路线）；ColorCode 为轻量替代 |
| 高亮 | [highlight.js](https://highlightjs.org) 11.x / [Prism](https://prismjs.com) 1.30 | BSD-3 / MIT | JS | ✅ Blazor 首选（`MutationObserver` 自动高亮新增块，适配流式）；Prism 需手动 `highlightAll` |
| 图 | [Mermaid](https://mermaid.js.org) 11.x（含 CLI 离线预渲染） | MIT | JS（依赖 DOM） | ⚠️ 仅 Web/Blazor；单次 10~100ms、未闭合会抛错；桌面用 CLI 构建期渲成 SVG |
| 图 | [D2](https://d2lang.com)/[Graphviz](https://graphviz.org) | MPL-2.0/EPL-1.0 | 原生 | ⚠️ D2 有文件级义务；Graphviz 需捆绑原生库 |
| 图 | PlantUML | **GPL-3.0** | Java | ❌ 传染性+Java 依赖 |

**建议**：公式与高亮分两条腿（Web=KaTeX+highlight.js，桌面=CSharpMath+AvaloniaEdit）；Mermaid 做可选插件，`dot`/`mermaid` 统一抽象为 `diagram` 节点。

## 5. Agent 交互式 UI 的现成规范/协议

**5.1 数据形态分类**

- **A. 事件流**：传「用户可见内容的增量事件」，UI 由客户端决定。代表 **AG-UI 1.0**（生命周期/文本增量/工具调用/推理/状态/活动/子代理/透传等 8 类事件族）。
- **B. 声明式组件树（catalog 约束）**：传整棵 UI 描述+组件白名单。代表 **A2UI**（声明式 DSL：扁平组件列表+ID 引用+数据模型）、**json-render**（JSON Schema 驱动：`{root,elements}`+catalog+`$state` 表达式）。
- **C. HTML/JS 资源+沙箱**：传可执行前端资源，iframe 隔离。代表 **MCP Apps**、**OpenAI Apps SDK**。
- **D. 框架组件层**：封装成前端组件与人类在环流程。代表 **CopilotKit**。

**5.2 对比**

| 协议/规范 | 数据形态 | Schema 驱动 | 可借鉴点 | 与「Markdown 内嵌声明式组件」的关系 |
|---|---|---|---|---|
| [AG-UI 1.0](https://docs.ag-ui.com/spec/1.0)（[repo](https://github.com/ag-ui-protocol/ag-ui)） | **事件流**（请求进、有序事件流出；HTTP+SSE/Protobuf） | ✅ 结构由 `schema.json` 定义 | 事件命名/生命周期；快照+增量；「模型输出不可信」 | **正交**：上游事件源 vs 渲染消费者；流式块事件可对齐命名以便桥接 |
| [MCP Apps（SEP-1865）](https://modelcontextprotocol.io/seps/1865-mcp-apps-interactive-user-interfaces-for-mcp)（Final） | **HTML 资源**：`ui://`+`text/html;profile=mcp-app`，iframe 沙箱+JSON-RPC | 否 | 预声明模板+工具关联；沙箱+可审计通信 | **过重**：只借「预声明+工具关联+最小权限」 |
| [OpenAI Apps SDK](https://developers.openai.com/apps-sdk/) | HTML/JS widget（MCP 骨干）+宿主桥 | 否 | 组件与工具解耦、widget↔宿主约定 | 同上，不引入 iframe |
| [A2UI](https://github.com/google/A2UI)（Apache-2.0，v0.9.1/v1.0 RC） | **声明式组件树**：扁平列表+ID 引用+数据模型+catalog | 部分 | 「安全如数据、表达如代码」；catalog 约束；扁平 ID 利于逐块生成 | **最同构**：它传整棵树，我们让片段成为 Markdown 文本流的一部分 |
| [json-render](https://github.com/vercel-labs/json-render)（Apache-2.0） | **声明式组件树**：`{root,elements}`+catalog（Zod→Schema）+`SpecStream` | ✅ 输出可校验 | catalog-as-guardrail；`SpecStream.push()`；`$state/$cond/$template` | **最可复用**：schema 校验、增量 patch、动态属性 |
| [CopilotKit](https://github.com/CopilotKit/CopilotKit)（MIT） | 框架组件层（基于 AG-UI） | — | 渲染钩子、interrupt/resume | 交互回传（多选确认）参考 |

**5.3 定位差异与可复用点**

我们的组件**不是整棵 UI 树，而是文本流中的若干片段**（如 fenced code 块 `agent-ui` 中内嵌 JSON/YAML）。优势：文本即协议——LLM 按 token 流式输出即可增量渲染，可读、可 diff、可复制、可降级为纯 Markdown。代价：无状态机与双向通信，交互须定义**显式 action 回传契约**（宿主桥把 `{componentId,action,payload}` 交给 Agent）；联动需自定义 `bind`；流式未闭合必须降级。

**可复用点**：① catalog+属性 schema（解析期校验、未知组件降级）；② 动态属性表达式（`$state` 子集）；③ 增量 patch（对齐 SpecStream/AG-UI state delta）；④ 事件命名与安全原则（AG-UI）；⑤ 组件预声明与工具关联（MCP Apps）。

## 6. 安全与许可

**6.1 许可矩阵（能否闭源商业分发）**

- ✅ **MIT/BSD/Apache（可闭源）**：Markdig、Markdown.Avalonia、Avalonia、AvaloniaEdit、TextMateSharp、MudBlazor、Radzen、Fluent UI Blazor、Ant Design Blazor、ScottPlot、LiveCharts2、KaTeX、Prism、Mermaid、Chart.js、highlight.js、Vega-Lite、ECharts（Apache，保留 NOTICE）。
- ⚠️ **MPL-2.0**：D2（文件级义务）。❌ **GPL-3.0**：PlantUML。❌ **商业订阅**：Avalonia Pro 的 Markdown 控件（下游须持有 license key）。

**6.2 桌面壳（WebView2/Blazor）CSP 与资源**

- 资源走本地映射（`SetVirtualHostNameToFolderMapping` 或自定义 `app://`），不能用 `file://`；JS/CSS/字体随包，禁 CDN。
- CSP：`script-src 'self'`；Blazor WASM 需 `'wasm-unsafe-eval'`（解释器模式还需 `'unsafe-eval'`）；KaTeX 注入 `<style>`，`style-src` 需允许 `'unsafe-inline'`；字体 `font-src 'self' data:`。禁原始 HTML、`javascript:` URL 与内联事件。

## 7. 结论：推荐组合与风险清单

**7.1 推荐组合（主推）**

| 层 | 选型 | 理由 |
|---|---|---|
| 解析 | Markdig 1.5.1（BSD-2） | 唯一活跃、可扩展、可替换渲染器 |
| 文档模型 | **PuraMarkdown IR（UI 无关）** | 组件描述符+来源 span+block 级增量，是流式与多适配器的前提 |
| 平台+UI | **Avalonia 12 + 自建交互组件**（Semi.Avalonia/Ursa 提供主题与控件），Markdown.Avalonia **仅作排版参考、不作为依赖** | 单一 .NET 栈、离线、无 CSP、AOT 友好、许可干净 |
| 图表 | **ScottPlot 5.1.59**（交互增强用 LiveCharts2） | 纯 .NET、MIT、离线、性能强 |
| 公式 | **CSharpMath**（复杂场景构建期预渲染兜底） | 无 JS 依赖 |
| 代码高亮 | **AvaloniaEdit + TextMateSharp** | MIT、主题完整、流式刷新可控 |

**7.2 三个备选组合与取舍**

| 方案 | 平台+UI | 图表 | 公式/高亮 | 取舍 |
|---|---|---|---|---|
| **B. Blazor 能力上限** | Blazor（Web/WebView2 壳）+MudBlazor 9.12 | ECharts（自封装 interop） | KaTeX+highlight.js | ✅ 组件最全、A11y/虚拟化最好、ECharts 最强；❌ JS interop+放宽 CSP+WASM 体积+双运行时调试 |
| **C. WPF 存量迁移** | WPF (.NET 8)+HandyControl/MahApps | LiveCharts2 2.0.4 | WpfMath+ColorCode | ✅ 存量团队上手快、DataGrid 成熟；❌ 平台前景弱、无 AOT、需迁移 |
| **D. 跨平台/Mobile** | MAUI 或 Uno+CommunityToolkit | ScottPlot/LiveCharts2 | CSharpMath | ✅ 一套代码覆盖移动端；❌ 无现成 Markdown 控件、控件与 A11y 质量差 |

**关键取舍**：优先「离线+许可干净+单一技术栈」，把「组件丰富度+Web 生态」放第二适配器。

**7.3 风险清单**

1. **AOT/裁剪**：SkiaSharp/ScottPlot/LiveCharts2 在 NativeAOT+`PublishTrimmed` 下需实测；`Svg.Skia`、字体资源易被裁。
2. **JS interop×CSP**：严格 CSP 会拦 KaTeX 内联样式、Blazor WASM 的 `wasm-unsafe-eval`、Mermaid DOM 注入；`file://` 直接使 ES module 失败。
3. **商业许可传染**：Avalonia Pro 控件要求下游持有 license key，误用即失去 MIT/Apache 定位。
4. **维护停滞/断代**：Markdig.Wpf 停更于 2021（绑 0.22）；`ChartJs.Blazor`/`Blazor.ECharts`/OxyPlot 迭代慢；Markdig 0.4x→1.0（2026-02）使绑旧版的下游库断代。
5. **版本强耦合**：Markdown.Avalonia 与 Avalonia 大版本一对一绑定（12.0.0↔12.0.2），升级牵连整条适配器。
6. **流式×虚拟化**：增量插入导致高度变化与滚动跳动，需底部锚定；变高度条目虚拟化易错位。
7. **内嵌 JSON 注入面**：属性必须白名单+长度/枚举校验（URL scheme、颜色、数值范围），否则退化为 CSS/URL 注入。
8. **公式与中文混排**：CSharpMath 对 `align`/矩阵/CJK 支持有限（待验证）；KaTeX 不支持部分宏，需「未知公式降级」。
9. **协议演进与双栈漂移**：AG-UI 1.0 刚定版、A2UI v1.0 RC、MCP Apps 刚 Final，语法需内置 `version`；IR→Avalonia 与 IR→Razor 须共享 golden 测试，自建单选/多选须补 `AutomationPeer`。
10. **体积**：ECharts+Mermaid+KaTeX 字体可达数 MB。

**7.4 待验证清单**

- `LiveMarkdown.Avalonia` 是否存在；WinUI 3/CommunityToolkit 8.x 是否有 Markdown 控件；Radzen `RadzenMarkdown`/`BlazorMarkdown` 的能力。
- CSharpMath 在 Avalonia+NativeAOT 下的表现与 LaTeX 覆盖率；ScottPlot/LiveCharts2/SkiaSharp 的裁剪行为；Avalonia Pro Markdown 的授权条款；CopilotKit/AG-UI 的许可证与 MudBlazor/Radzen 最近提交日期。
