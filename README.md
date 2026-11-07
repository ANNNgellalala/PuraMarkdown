# PuraMarkdown

> 高性能 **Avalonia 只读 Markdown 渲染库**：Markdig 解析、Avalonia 原生渲染、增量流式、块级/消息级虚拟化、插件可替换。Agent 交互与聊天控件以独立插件包提供。
>
> 状态：**设计定稿（v0.4）+ M0–M4 已跑通 + 1.0-alpha 就绪**（105 个测试、7 个 NuGet 包、Agent 已插件化、Chat 独立包、Native AOT/裁剪验证通过、CI 就绪）。
> 框架：**.NET 10（net10.0，本机 SDK 10.0.114）** · Avalonia **12.1.5** · Markdig **1.5.1** · ColorCode.Core **2.0.15** · Mermaider **0.15.1**；JsonSchema.Net 仅由 `PuraMarkdown.Agent` 使用。

---

## 它解决什么

Agent 回复不只是"一段 Markdown"：

- **交互**：单选/多选/确认卡片，选择结果事件上报宿主；
- **结构化内容**：工具结果、图表围栏（由插件渲染）；
- **流式**：逐 token 追加，块级增量，不整篇重解析；
- **不可信输入**：畸形/超长/恶意 Markdown 也要可读、安全；
- **可扩展**：代码高亮、图表引擎、自定义组件都可插拔。

## 核心思路（四层）

```
Markdown 文本（全量 or Append 增量）
   │  ① Markdig 解析（CommonMark+GFM, DisableHtml, 精确 Span）
   ▼
Markdig AST + 插件节点（围栏/行内/块）
   │  ② 节点分发：渲染器注册表按优先级匹配
   ▼
③ 渲染执行：IPuraRenderer → Avalonia Control
   │
   ▼
④ 流式会话：稳定边界 + 尾部窗口 + 块级嫁接
```

**没有中间 IR**：Markdig AST 就是文档模型，渲染器直接产出 Avalonia 控件（ADR-0011）。基础渲染器只是注册表中 priority 0 的默认项，可被插件覆盖。

## Agent 扩展语法

主路径是**围栏 + JSON**（对 Markdig 负担最小，降级为普通代码块）：

````markdown
```pura-agent
{
  "type": "choice",
  "version": "1.0",
  "id": "env",
  "question": "选择部署环境",
  "multi": false,
  "options": [
    { "id": "dev",  "label": "开发" },
    { "id": "prod", "label": "生产", "description": "需要审批", "disabled": true }
  ]
}
```
````

`choice`/`confirm` 由 `PuraMarkdown.Agent` 包提供；其它组件通过 `builder.UseAgentComponent(parser, renderer)` 扩展。容器指令（`:::choice`）不在核心范围，可由插件实现。

## 主要契约

| 契约 | 作用 |
| --- | --- |
| `IPuraRenderer` | `CanRender(node, ctx)` + `Render(node, ctx) → Control`；可选 `IIncrementalPuraRenderer` |
| `IFenceRenderer` | 接管 ```lang 围栏（mermaid/pura-agent/自定义）→ 控件；无插件回退代码块 |
| `IAgentComponentParser` | `pura-agent` JSON 解析与 Schema 校验（`PuraMarkdown.Agent` 包） |
| `IAgentComponentRenderer` | Agent 组件渲染 + 交互事件（`PuraMarkdown.Agent` 包） |
| `IPuraInlineRenderer` | 自定义行内语法渲染 |
| `IAstSanitizer` | AST 清洗/阻断（`UseSanitizer` 可替换） |
| `ICodeHighlighter` | 代码 token 化；默认纯等宽 |
| `IPuraPlugin` / `PuraPipelineBuilder` | 插件注册（围栏/行内/块/高亮/清洗） |
| `PuraMarkdownView` / `PuraStreamingSession` | 控件与 append-only 流式 API |

## 包结构

| 包 | 内容 |
| --- | --- |
| `PuraMarkdown` | 渲染层：控件、默认渲染、流式、虚拟化、插件契约 |
| `PuraMarkdown.Syntax` | 语法层：Markdig 解析、`PuraDocument`、诊断、大纲、扫描；无 Avalonia |
| `PuraMarkdown.Agent` | `pura-agent` 围栏、JSON 组件、choice/confirm、Agent 本地化 |
| `PuraMarkdown.Chat` | 聊天控件：消息级虚拟化、流式槽、自动滚动 |
| `PuraMarkdown.Testing` | 无头渲染宿主、控件树 dump、golden 工具、交互/流式测试驱动 |
| `PuraMarkdown.Highlighting.ColorCode`（可选） | `ICodeHighlighter` 实现 |
| `PuraMarkdown.Diagrams.Mermaid`（可选） | `IFenceRenderer` 实现（Mermaider） |
| `samples/PuraMarkdown.Demo` | 演示应用（流式 + 交互 + 主题 + 插件） |

## 使用方式

```csharp
using PuraMarkdown.Agent;

// 启动时构建一次
var pipeline = new PuraPipelineBuilder()
    .UseCommonMark()
    .UseGfm()
    .UseAgentComponents()                             // 需要 PuraMarkdown.Agent 包
    .UseCodeHighlighter(new ColorCodeHighlighter())   // 可选
    .UsePlugin(new MermaidPlugin())                   // 可选
    .Build();

var view = new PuraMarkdownView
{
    Options = new PuraMarkdownOptions { Pipeline = pipeline, Theme = PuraTheme.Light }
};
view.InteractionOccurred += (_, e) => host.HandleChoice(e);

// 历史消息
view.Markdown = message.Text;

// 流式
var session = view.BeginStreaming();
session.Append(delta);
await session.CompleteAsync();
```

## 当前代码（M0–M4 + 公共 API 收敛）

```bash
dotnet build PuraMarkdown.slnx
dotnet test tests/PuraMarkdown.Tests/PuraMarkdown.Tests.csproj
dotnet run --project samples/PuraMarkdown.Spike/PuraMarkdown.Spike.csproj
```

```
PuraMarkdown.slnx
├─ src/PuraMarkdown.Syntax/             # 语法层：Markdig 解析、PuraDocument、诊断/大纲/扫描（无 Avalonia）
├─ src/PuraMarkdown/                     # 渲染层：控件、默认渲染、流式、虚拟化、插件契约
├─ src/PuraMarkdown.Agent/               # 官方 Agent 插件：pura-agent 围栏、JSON 组件、choice/confirm
├─ src/PuraMarkdown.Chat/                # 聊天控件：消息虚拟化、流式槽
├─ src/PuraMarkdown.Testing/             # 无头 dump / golden / 交互探针 / 流式驱动 / 插件一致性
├─ src/PuraMarkdown.Highlighting.ColorCode/  # 官方高亮插件（ColorCode.Core）
├─ src/PuraMarkdown.Diagrams.Mermaid/    # 官方 Mermaid 插件（Mermaider，纯 .NET）
├─ samples/PuraMarkdown.Plugins.Samples/ # 示例插件：rating 组件、示例图表
├─ samples/PuraMarkdown.ArcSample/       # 仅引用 Syntax 的 Arc 风格语法树消费示例
├─ tests/PuraMarkdown.Tests/             # 无头 Avalonia + 105 个测试 + golden
├─ benchmarks/PuraMarkdown.Benchmarks/   # BenchmarkDotNet 基准
└─ samples/PuraMarkdown.Spike/           # 离屏渲染示例（打印控件树）
```

当前实现：Markdig GFM 管道、渲染器注册表与默认渲染组、`BlockRecord`、**稳定边界 + 尾部窗口增量流式**、块级/消息级虚拟化、`IFenceRenderer`/行内/块插件契约、`UseSanitizer`、ColorCode 高亮、Mermaid 插件、Agent 独立包（`pura-agent` 围栏 + choice/confirm + 组合式自定义组件）、Chat 独立包、文本选择/复制/大纲、链接/图片事件、主题与本地化、安全预算。

## Demo

**GUI 演示应用**（流式追加、深浅色切换、choice/rating 交互日志、ColorCode/Mermaid 插件）：

```bash
dotnet run --project samples/PuraMarkdown.Demo/PuraMarkdown.Demo.csproj
```

**无头截图**（可复现，不依赖桌面）：

```bash
dotnet run --project samples/PuraMarkdown.Spike -- --screenshot docs/images/demo-light.png
dotnet run --project samples/PuraMarkdown.Spike -- --screenshot docs/images/demo-dark.png --dark
```

浅色主题：

![PuraMarkdown 浅色主题 Demo](docs/images/demo-light.png)

深色主题：

![PuraMarkdown 深色主题 Demo](docs/images/demo-dark.png)

## 文档导航

> 应用接入请优先阅读 26/27/28；其余为历史设计与里程碑记录，代码示例可能使用已删除 API（如 `IDiagramRenderer`、`PuraAgentBlock`）。

| 文档 | 内容 | 适合谁 |
| --- | --- | --- |
| [应用集成指南](docs/26-应用集成指南.md) | 包选择、最小接入、Agent/Chat、流式、常见坑 | **APP 接入优先** |
| [迁移指南（alpha.1）](docs/27-迁移指南-0.1.0-alpha.1.md) | 从旧版（Agent 在核心/IDiagramRenderer）升级 | 现有宿主 |
| [公共 API 参考](docs/28-公共API参考.md) | 宿主/插件/Chat API 签名与稳定性分级 | 宿主/插件作者 |
| [文档索引](docs/README.md) | 当前有效 vs 历史文档 | 所有人 |
| [版本与升级策略](docs/29-版本与升级策略.md) | 包版本矩阵、API 稳定性分级、APP 升级步骤 | 宿主维护者 |
| [Agent 与 Chat 能力增强](docs/30-Agent与Chat能力增强.md) | 会话模型/快照/流式状态，Agent 状态持久化/组件目录，不含新 UI | 宿主/Arc.Avalonia |
| [语法与渲染分离讨论](docs/31-语法与渲染分离讨论.md) | 类 IDE/LSP 的 Syntax/Renderer 拆分、语义文档模型与迁移路径 | 架构/Arc.Avalonia |
| [技术演进路径 B→C](docs/32-技术演进路径-B到C.md) | B 阶段工作包/验收，B 完成后的 C 演进路径 | 架构/排期 |
| [B 阶段实施记录](docs/33-B阶段实施记录.md) | B0–B5 落地内容、验证与剩余硬化项 | 实现者/验收 |
| [B 阶段基线：API/Trim/性能](docs/34-B阶段基线-API-Trim-性能.md) | PublicAPI baseline、Trim/AOT 结果、性能对比表 | 发布/验收 |
| [需求方案（用户输入）](docs/PuraMarkdown需求方案.md) | 原始需求与边界定义 | 所有人 |
| [需求对齐与设计复审](docs/00-需求对齐与设计复审.md) | v0.2 → v0.3 的差异、6 项已确认决策、IR/虚拟树/适配层讨论结论 | 决策者 |
| [IR/虚拟树/多平台讨论](docs/07-IR-虚拟渲染树-多平台-讨论与决策.md) | 三项架构取舍的分析、触发条件、回补成本 | 决策者/架构 |
| [需求规格](docs/01-requirements.md) | 65 条 FR、15 条 NFR、M0–M4、决策与暂缓项 | 产品/验收 |
| [架构设计](docs/02-architecture.md) | 四层架构、契约、流式、安全、测试、版本基线 | 实现者 |
| [Agent 扩展语法规范](docs/03-agent-syntax-spec.md) | `pura-agent` 围栏、choice/confirm schema、事件、降级 | 实现者/提示词作者 |
| [插件与渲染契约](docs/04-plugin-and-rendering.md) | 契约定义、注册表/优先级、默认渲染组、示例插件 | 插件作者 |
| [路线图与风险](docs/05-roadmap-and-risks.md) | 版本基线、里程碑、验收、风险、工作量 | 项目管理 |
| [需求→设计对照](docs/06-traceability.md) | FR/NFR 落点与缺口 | 验收 |
| [M0 实现进展](docs/08-M0-实现进展.md) | 已完成范围、验证结果、与设计的偏差、M1 计划 | 实现者/验收 |
| [M1 实现进展](docs/09-M1-实现进展.md) | 自定义解析器、schema 校验、链接/图片/任务列表、主题/A11y、golden 基线 | 实现者/验收 |
| [M2 实现进展](docs/10-M2-实现进展.md) | 插件校验与回退链、ColorCode 高亮插件、图表/rating 示例插件、Testing 工具包 | 实现者/验收 |
| [M3 实现进展](docs/11-M3-实现进展.md) | 稳定边界+尾部窗口增量流式、键盘/播报、真实 Mermaid 引擎、性能烟测 | 实现者/验收 |
| [M4 实现进展](docs/12-M4-实现进展.md) | 1MB/1000 块、安全语料、A11y、示例应用、NuGet 打包、裁剪验证 | 实现者/验收 |
| [1.0 发布准备](docs/13-1.0-发布准备.md) | 基准数据、AOT/裁剪、CI、API 文档现状、发布清单 | 发布负责人 |
| [渲染性能问题分析](docs/14-渲染性能问题分析.md) | 流式卡顿复现数据、根因分级与修复建议 | 架构/性能 |
| [渲染性能修复记录](docs/15-渲染性能修复记录.md) | P0 四项修复内容、前后对比数据与遗留项 | 架构/性能 |
| [多轮长对话性能验证](docs/16-多轮长对话性能验证.md) | 1000 条消息/1.8MB 场景下的耗时、内存与宿主模式建议 | 架构/性能 |
| [消息级虚拟化实现](docs/17-消息级虚拟化实现.md) | ItemsRepeater 虚拟化、1000 条消息 realized 数量与内存对比、宿主建议 | 架构/性能 |
| [块级虚拟化实现](docs/18-块级虚拟化实现.md) | 单条 1MB/1000 块：默认 69.8MB/2.65s → 1.6MB/0.45s | 架构/性能 |
| [Git 版本管理与发布流程](docs/19-Git版本管理与发布流程.md) | 分支、提交规范、版本号、tag 与发布步骤 | 贡献者/发布负责人 |
| [Markdown 完整性检查](docs/20-Markdown完整性检查.md) | 软换行/强制提交等修复记录与剩余 P0/P1/P2 清单 | 架构/质量 |
| [公共 API 与功能扩展建议](docs/21-公共API与功能扩展建议.md) | API 一致性、宿主集成、扩展点与内容语法建议 | 架构/产品 |
| [P1 功能与本地化实现](docs/22-P1功能与本地化实现.md) | 流式生命周期、选择/复制、大纲、事件、主题、中文默认本地化 | 实现者/验收 |
| [公共 API 设计评审](docs/23-公共API设计评审.md) | API 分层、1.0 前收敛项、扩展点与开放问题 | 架构/决策 |
| [Agent 插件化与公共 API 收敛实现](docs/25-Agent插件化与公共API收敛实现.md) | IFenceRenderer/行内/清洗器契约、Agent/Chat 独立包、迁移指南 | 实现者/验收 |
| [插件通用接口与 Agent 插件化讨论](docs/24-插件通用接口与Agent插件化讨论.md) | IFenceRenderer/行内/块级插件契约、Agent 剥离、Chat 包边界 | 架构/决策 |
| [示例 Agent 回复](samples/agent-conversation.md) | 新语法样例 | 实现者 |
| [Markdig 调研](docs/research/markdig.md) / [生态调研](docs/research/ecosystem.md) | 技术事实与选型依据 | 实现者 |
| [ADR](docs/adr/) | 决策记录（0011–0016 为 v0.3 新决策） | 所有人 |
| [v0.2 评审报告](docs/review-report.md) | 历史评审（针对旧 IR 架构，结论已不适用） | 追溯 |

## 关键决策（v0.3）

- **无 IR**：Markdig AST 直连 Avalonia 控件；补 `IAstSanitizer` 净化 pass（ADR-0011、ADR-0020）。
- **`pura-agent` 围栏 + JSON** 为扩展语法主路径（ADR-0012）。
- **net10.0** + 最新稳定依赖；使用本机 SDK 10.0.114，不下载新 SDK（ADR-0013）。
- **图表/公式/高亮全部插件化**，核心零引擎依赖；后续给官方默认高亮插件（ADR-0014）。
- **流式 = 块级脏检查 + 尾部窗口 + 视觉树嫁接**（ADR-0015）。
- **核心直接依赖 Avalonia，跨平台交给 Avalonia**（Win/macOS/Linux）；不建渲染适配层、不建虚拟渲染树（ADR-0016、ADR-0021/0022）。
- **不渲染 HTML**（ADR-0009）、**append-only**（ADR-0010）、**交互只产事件**（ADR-0006）。
- **块级虚拟化可选**（`EnableBlockVirtualization`）；消息级虚拟化见 `PuraMarkdown.Chat`。
- **Agent 语法独立为插件包**（`PuraMarkdown.Agent`），核心只做 Markdown（ADR-0024）。

> IR/虚拟渲染树/多平台适配三项的取舍、触发条件与回补成本：见 [讨论文档](docs/07-IR-虚拟渲染树-多平台-讨论与决策.md)。

## 本地开发

```bash
dotnet --version     # 10.0.114
dotnet build
dotnet test
# M0 后：dotnet run --project samples/PuraMarkdown.Demo
```

要求：.NET 10 SDK、Avalonia 12.1.5。

## 许可证

待定（建议 MIT）。第三方依赖须 MIT/BSD/Apache-2.0；详见需求 §8 约束 C-4。









































