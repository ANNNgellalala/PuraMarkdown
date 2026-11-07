> 历史文档：记录当时的设计/实现，API 可能已过时。当前接入请阅读 [应用集成指南](26-应用集成指南.md) / [迁移指南](27-迁移指南-0.1.0-alpha.1.md) / [公共 API 参考](28-公共API参考.md)。

# 路线图、版本基线与风险（v0.4）

> 状态：设计定稿 v0.4（6 项产品决策 + 3 项架构取舍均已确认；IR/虚拟树/适配层不进入 1.0，见 §8）
> 关联：[需求对齐](./00-需求对齐与设计复审.md) · [需求规格](./01-requirements.md) · [架构](./02-architecture.md) · [语法](./03-agent-syntax-spec.md) · [插件](./04-plugin-and-rendering.md)

---

## 1. 版本基线（2026-11-05 实测最新稳定版）

| 项 | 版本 | 说明 |
| --- | --- | --- |
| .NET SDK | **10.0.114** | **使用本机已安装版本，不下载/不安装额外 SDK**；`global.json` 锁定，`rollForward: latestFeature` |
| TFM | **net10.0** | 全部包与示例单目标 |
| Avalonia | **12.1.5** | 提供 net10.0 资产（已核对 nuspec） |
| Avalonia.Desktop / Themes.Fluent / Headless.XUnit | 12.1.5 | 同版本 |
| Markdig | **1.5.1** | BSD-2；解析内核 |
| Microsoft.Extensions.DependencyInjection / Options / Logging | **10.0.14** | DI/配置/日志 |
| JsonSchema.Net | **9.6.0** | `pura-agent` 载荷校验 |
| Avalonia.AvaloniaEdit | **12.0.0** | 可选（TextMate 高亮插件） |
| Avalonia.AvaloniaEdit.TextMate | **12.0.0** | 可选 |
| TextMateSharp / TextMateSharp.Grammars | **2.0.5** | 可选 |
| ColorCode.Core | **2.0.15** | 可选轻量高亮 |
| ScottPlot.Avalonia | **5.1.59** | 可选图表插件（M3） |
| CSharpMath.Avalonia | **0.5.1** | 可选公式插件（1.1） |
| Mermaider / Naiad | **0.15.1 / 2.0.1** | 可选 Mermaid 候选（M2 spike） |
| SkiaSharp | **4.154.0** | 图表/图形插件底层（按需） |
| Avalonia.Controls.ItemsRepeater | **12.0.0** | 可选块虚拟化（M3 验证） |
| xunit.v3 / Microsoft.NET.Test.Sdk / BenchmarkDotNet | **4.0.2 / 18.11.0 / 0.15.8** | 测试/基准 |
| CommunityToolkit.Mvvm | **8.4.2** | 示例应用 |

**版本策略**：`Directory.Packages.props` 集中管理；M0 锁定后写入提交；升级必须过回归（解析样例、golden dump、流式等价性、基准阈值）。

---

## 2. 里程碑（与提案的 4 步落地顺序对应）

### M0 — 解析路由 + 基础渲染 + 块宿主（1–2 周）

对应提案第一步。**状态：已完成纵向切片**（见 [M0 实现进展](08-M0-实现进展.md)）：标准 Markdown、块宿主、`pura-agent` choice/confirm、流式骨架、3 个钩子、12 个无头测试通过。

- [ ] 工程骨架：`PuraMarkdown`、`Testing`、`samples/spike`；`global.json`、`Directory.Packages.props`
- [ ] Markdig 管道：CommonMark + GFM + `UsePreciseSourceLocation` + `DisableHtml`
- [ ] 渲染器注册表 + `IPuraRenderer` 分发骨架（默认渲染器 priority 0）
- [ ] 默认渲染组：段落/标题/列表/任务列表/引用/代码块/表格/分隔线/链接/图片/HTML 转义
- [ ] 块宿主：按顶层块组织视觉树（`StackPanel`），`BlockRecord`（span/hash/node/view）
- [ ] 预留钩子：`IAstSanitizer` 净化 pass、`IPuraPlatformServices`（剪贴板/字体/播报）
- [ ] 主题：Fluent Light/Dark token → 控件属性；跟随系统
- [ ] 测试骨架：无头渲染 + `ControlTreeDumper` + 首批 golden
- [ ] 流式等价性测试骨架（先覆盖一次性渲染路径）

**退出标准**：标准 Markdown 样例集（≥50 个文档）渲染 dump 与预期一致；版本组合锁定；安全净化 pass 有测试；无高危安全问题。

### M1 — `pura-agent` + choice/confirm（1–2 周）

对应提案第二步。**状态：主体完成**（见 [M1 实现进展](09-M1-实现进展.md)）：自定义 `PuraAgentBlockParser`、JsonSchema.Net 校验、链接/图片/任务列表、主题跟随、A11y 名称、4 个 golden 基线；25 个测试通过。键盘端到端与播报留到 M2。

- [ ] `PuraAgentBlockParser`（识别 `pura-agent` 围栏、`IsClosed` 标记）
- [ ] `IAgentComponentParser` + `JsonSchema.Net` 校验 + 错误定位
- [ ] `choice` 单选/多选：默认渲染、`min`/`max`、disabled、默认选中、提交按钮
- [ ] `confirm`：确认/取消、danger 样式
- [ ] 交互事件 `InteractionOccurred` + 只读模式
- [ ] 降级卡（非法 JSON/schema 失败/未知 type）
- [ ] 键盘交互 + A11y 语义
- [ ] 演示：手工构造 `pura-agent` 消息，点击后打印事件

**退出标准**：choice/confirm 端到端可用；键盘完成交互；非法载荷降级正确；事件载荷符合 schema。

### M2 — 插件机制 + 高亮/图表插件（2 周）

对应提案第三步。**状态：主体完成**（见 [M2 实现进展](10-M2-实现进展.md)）：插件校验/回退链、ColorCode 高亮插件、图表契约与示例插件、rating 自定义组件、Testing 工具包；37 个测试通过。真实 Mermaid 引擎 spike 与键盘/播报留到 M3。

- [ ] `IPuraPlugin` + `PuraPipelineBuilder` 注册校验（fail fast）
- [ ] `IIncrementalPuraRenderer`（代码块/组件更新）
- [ ] `ICodeHighlighter` + `PuraMarkdown.Highlighting.ColorCode` 插件
- [ ] `IDiagramRenderer` + Mermaid 插件 spike（Mermaider vs Naiad，评估 TFM/AOT/性能/许可）
- [ ] 自定义 Agent 组件示例（`rating`）验证契约
- [ ] 插件一致性测试套件

**退出标准**：接入真实高亮/图表引擎无需修改契约；插件异常回退链可验证。

### M3 — 流式会话（2 周）

对应提案第四步。**状态：主体完成**（见 [M3 实现进展](11-M3-实现进展.md)）：稳定边界 + 尾部窗口增量流式、键盘/播报、真实 Mermaid 引擎（Mermaider）、100KB 性能烟测；51 个测试通过。块虚拟化与 SVG 高保真显示留到 M4。

- [ ] `PuraStreamingSession`：`Append`/`CompleteAsync`/`AbortAsync`、线程安全、合帧
- [ ] 稳定边界扫描 + 尾部窗口重解析（8–16KB）
- [ ] 块级 diff + 增量嫁接（新增/替换/更新）
- [ ] 未闭合围栏/半截 JSON 占位与完成校正
- [ ] 滚动跟随策略（Follow/Anchor/Manual）
- [ ] 长消息性能：1MB 文档、1000 块；虚拟化验证（可选）
- [ ] 属性测试：逐段追加 vs 一次性渲染 dump 等价

**退出标准**：NFR-01/02/04/05 达标；等价性测试 1 万次通过；无交互状态错乱。

### M4 — 1.0 硬化（2 周）

**状态：主体完成**（见 [M4 实现进展](12-M4-实现进展.md)）：1MB/1000 块、安全语料、A11y 基础、示例应用、4 个 NuGet 包、`PublishTrimmed` 验证通过；60 个测试通过。剩余：BenchmarkDotNet 基线、屏幕阅读器实测、Native AOT、API 注释补齐。
- [ ] 性能/内存调优；大文档与长会话压测
- [ ] 安全语料（HTML/URL/超长/深嵌套/伪造交互）全绿
- [ ] A11y 走查（键盘/读屏/对比度/减少动效）
- [ ] 示例应用（流式 + 交互 + 主题 + 插件）
- [ ] 文档：快速开始、插件作者指南、迁移说明（如需要）
- [ ] 打包：核心/测试/插件包、SemVer、NuGet 元数据
- [ ] API 审查 + 公开 API XML 注释

**退出标准**：NFR 全量达标；1.0 发布清单完成；示例可离线运行。

---

## 3. 验收标准（1.0）

| 维度 | 标准 |
| --- | --- |
| 功能 | §6 所有 Must 通过；Should ≥ 90% |
| 语法 | `pura-agent` 样例全通过；未知/非法载荷降级可读 |
| 安全 | 0 脚本、0 网络、0 崩溃；1MB 恶意输入 UI 阻塞 ≤ 500ms |
| 性能 | NFR-01/02/04/05/06 达标（P95，基准机固定） |
| 平台 | net10.0 + Avalonia 12.1.5；`PublishTrimmed` 示例可运行 |
| 可测试 | golden dump 基线、流式等价性、交互探针均接入 CI |
| 质量 | 核心覆盖率 ≥ 80%；无 P0/P1 缺陷 |
| 文档 | 快速开始 30 分钟跑通；插件作者 1 人天完成插件 |

---

## 4. 风险登记册

| # | 风险 | 概率 | 影响 | 应对 | 触发信号 |
| --- | --- | --- | --- | --- | --- |
| R1 | Markdig AST 跨版本不兼容 | 中 | 中 | 锁 1.5.1；只消费公开节点；升级跑回归 | 升级后编译/快照失败 |
| R2 | 无 IR 导致安全过滤分散 | 中 | 高 | 默认渲染器强制 URL/HTML；安全语料；代码评审清单 | 出现绕过路径 |
| R3 | 流式边界误判（列表/围栏/表格） | 中高 | 高 | 尾部窗口 + 属性测试 + 保守提交 | 等价性测试失败 |
| R4 | 插件渲染异常/阻塞 UI | 中 | 高 | 回退链 + warning；渲染器禁止 I/O（评审） | 用户报告卡顿/崩溃 |
| R5 | 交互状态在流式重渲染中丢失 | 中 | 中 | 完成前禁用交互；优先 `Update` 而非替换 | 选项被重置 |
| R6 | 无虚拟化时长消息卡顿 | 中 | 中 | M3 实测；`ItemsRepeater` 可选；先保正确性 | 1000 块滚动掉帧 |
| R7 | Avalonia 12.1.x 之后的破坏性变更 | 中 | 中 | 锁版本；控件工厂集中；升级专项 | 升级编译错误 |
| R8 | 图表引擎不可用（TFM/AOT/许可） | 中 | 中 | M2 spike 多候选（Mermaider/Naiad/ScottPlot）；回退代码块 | spike 不达标 |
| R9 | JSON Schema 校验开销 | 低 | 中 | 只校验组件块；schema 预编译；超限直接降级 | 追加延迟超标 |
| R10 | 范围蔓延（又想做编辑/多平台） | 中 | 中 | 严格非目标；新需求走 ADR | 需求单超出 §6 |

---

## 5. 工作量估算（单人全职，参考）

| 阶段 | 内容 | 人日 |
| --- | --- | --- |
| M0 | 骨架 + 解析路由 + 默认渲染 + 块宿主 + 3 个预留钩子 + 测试基线 | 12–16 |
| M1 | `pura-agent` + choice/confirm + 事件 + A11y | 8–12 |
| M2 | 插件机制 + 高亮/图表插件 + 自定义组件示例 | 10–14 |
| M3 | 流式会话 + 增量嫁接 + 性能 | 10–14 |
| M4 | 硬化 + 安全 + 文档 + 打包 | 8–12 |
| **合计** | 1.0 | **约 48–68 人日** |

> 比 v0.2（70–95 人日）明显下降，主要来自：去掉 IR/适配器/多平台、扩展组件收敛为 2 个核心类型、图表与高亮外置。若未来加回 IR/虚拟树/适配层，按 §8 的回补成本累加（+8–10 周）。

---

## 6. 成功指标（发布后 3 个月）

| 指标 | 目标 |
| --- | --- |
| 接入宿主应用 | ≥ 2（自用 + 1 外部） |
| 第三方插件 | ≥ 1（非作者开发：高亮/图表/自定义组件） |
| P0/P1 缺陷 | 0 / ≤ 2 |
| 追加延迟（宿主上报 P95） | ≤ 16ms |
| 新增插件耗时 | ≤ 1 人日（含测试） |
| 公开 API XML 文档覆盖 | 100% |

---

## 7. 决策记录

| # | 决策 | 结论 | 记录 |
| --- | --- | --- | --- |
| D1 | 平台 | 核心直接依赖 Avalonia 12.1.5；跨平台由 Avalonia 承担（Win/macOS/Linux），不建适配层 | ADR-0016、ADR-0022 |
| D2 | 文档模型 | Markdig AST 直连渲染，无 IR；补 `IAstSanitizer` 净化 pass | ADR-0011、ADR-0020 |
| D3 | 扩展语法 | `pura-agent` 围栏 + JSON 主路径；容器指令为插件 | ADR-0012 |
| D4 | 框架/依赖 | net10.0（本机 SDK 10.0.114，不下载新 SDK）+ 最新稳定版（§1） | ADR-0013 |
| D5 | 图表/公式/高亮 | 全部插件化；核心不定义图表 schema；高亮后续给官方默认插件 | ADR-0014 |
| D6 | 流式 | append-only；块级脏检查 + 尾部窗口 + 视觉树嫁接 | ADR-0015、ADR-0010 |
| D7 | 交互 | 只产事件，传输归宿主；单选即报、多选确认 | ADR-0006 |
| D8 | HTML | 1.0 不渲染、不提供子集 | ADR-0009 |
| D9 | 虚拟渲染树 | 1.0 不引入；`BlockRecord` + 块级嫁接 | ADR-0021 |
| D10 | 长消息虚拟化 | 当前 `StackPanel`，M3 按实测再优化 | 架构 §13 |

---

## 8. 暂缓项（Deferred Backlog，带触发条件）

| 项 | 当前 | 触发条件 | 回补成本 |
| --- | --- | --- | --- |
| IR（文档模型） | 不做 | 非 Avalonia 后端 / 服务端渲染 / 节点级缓存 / Markdig 不可适配 breaking | 3–4 周 |
| 虚拟渲染树 | 不做 | 多后端 / 块内超长内容 / 渲染结果序列化 | 2–3 周 |
| 渲染适配层 | 不做 | Blazor/MAUI/非 Avalonia 宿主 / Avalonia 平台限制 | 3–4 周 |
| 官方高亮插件 | M2 后 | 默认高亮体验有需求 | 3–5 人日 |
| 数学公式插件 | 1.1 | 有真实公式需求 | 3–5 人日 |
| 块级虚拟化 | M3 评估 | 1000 块滚动 < 55FPS | 3–5 人日 |
| 容器指令插件 | 可选 | 社区/宿主偏好容器语法 | 5–8 人日 |

> 三项架构级暂缓项的完整分析见 [`07-IR-虚拟渲染树-多平台-讨论与决策.md`](07-IR-虚拟渲染树-多平台-讨论与决策.md)；M0 实现三个预留钩子（约 2–3 人日）。

---

## 9. 下一步（立即可做）

1. 确认 ADR-0020/0021/0022（建议采纳"1.0 都不加，只做钩子"）。
2. 建 `samples/spike`：跑通 "Markdig → 默认渲染 → 块宿主" 与一个 `pura-agent` choice 的最小闭环。
3. 建 `global.json`（10.0.114，不下载新 SDK）/ `Directory.Packages.props`，锁定 §1 版本。
4. 实现三个预留钩子：`IAstSanitizer`、`BlockRecord`、`IPuraPlatformServices`。
5. 建无头测试工程，产出首批 golden dump 与流式等价性测试骨架。

