# ADR-0028：包身份重命名——`PuraMarkdown.Core` / `PuraMarkdown.Agent` / `PuraMarkdown.Extension`

- 状态：已接受（2026-11-10）
- 关联：ADR-0027（7 → 3 包收敛；本 ADR 在其之上重排包 id 与命名空间，收敛结论不变）、ADR-0025（语法/渲染分离；`PuraMarkdown.Core` 保持无 Avalonia 是该决策的直接延续）、ADR-0014（核心零引擎依赖；引擎依赖仍只落在 `PuraMarkdown.Extension`）、ADR-0024（Agent 独立包；其"独立包"部分已由 ADR-0027 调整）
- 影响文档：[README](../../README.md)、[CHANGELOG](../../CHANGELOG.md)、[应用集成指南](../26-应用集成指南.md)、[迁移指南](../27-迁移指南-0.1.0-alpha.1.md)、[公共 API 参考](../28-公共API参考.md)、[版本与升级策略](../29-版本与升级策略.md)、[B 阶段基线](../34-B阶段基线-API-Trim-性能.md)、[节点矩阵](../35-Markdig支持节点子集与兼容矩阵.md)、[功能覆盖](../36-Markdown功能覆盖与缺口.md)、[文档索引](../README.md)、[ADR 索引](README.md)

## 背景

ADR-0027 把 7 个 NuGet 包收敛为 3 个，但**刻意保持 C# 命名空间不变**，以降低当时的升级成本。收敛后的包与命名空间因此严重错位：

- 裸 `PuraMarkdown` 既是 Avalonia 渲染控件包，又内置了原 `PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing` 的全部内容，包名既不表达"UI 控件"，也不表达"Agent/Chat"；
- 语法层包叫 `PuraMarkdown.Syntax`，却同时承载 `PuraDocument`、诊断、大纲、流式扫描等"基础层"职责，且是**唯一不依赖 Avalonia** 的包，名字看不出这是能力承诺；
- `PuraMarkdown.Plugins` 实际是官方扩展（ColorCode 高亮 + Mermaid 图表），`Plugins` 与本项目 `IPuraPlugin` 的"第三方插件"语义混叠；
- 宿主必须记住"装 `PuraMarkdown` 才 `using PuraMarkdown.Agent`"这类反直觉映射；文档只能长期维护一张"包 → 命名空间"对照表来解释错位；
- `PuraMarkdown` 这个**最像"产品本身"的包 id 被 UI 渲染层占用**，导致"PuraMarkdown 不提供 UI 控件"的口径永远无法成立，而服务端/无头解析场景本应只看到语法层。

时机：这些包只做过本地 `dotnet pack` 验证，**从未发布到 nuget.org**，包 id 与命名空间都还能一次性调整；一旦 1.0 发布，改名就是 MAJOR + 旧命名空间长期保留的兼容包袱。

## 决策

1. **包身份按职责重排为 3 个包**，版本统一保持 `0.1.0-alpha.1`（尚未发布，不升版本、不做兼容层）：

   | 旧 | 新 | 定位 | 依赖 |
   | --- | --- | --- | --- |
   | `PuraMarkdown.Syntax` | `PuraMarkdown.Core` | 基础语法层：Markdig 解析、`PuraDocument`、大纲、诊断、流式扫描；**无 Avalonia 依赖** | Markdig 1.5.1 |
   | `PuraMarkdown` | `PuraMarkdown.Agent` | Avalonia 渲染控件 + Agent 交互 + Chat 消息 + 工具调用（tool call）语法 | `PuraMarkdown.Core`、Avalonia 12.1.5、Avalonia.Controls.ItemsRepeater 12.0.0、Markdig 1.5.1、JsonSchema.Net 9.6.0 |
   | `PuraMarkdown.Plugins` | `PuraMarkdown.Extension` | ColorCode 代码高亮 + Mermaid 图表；渲染引擎依赖只在此包 | `PuraMarkdown.Agent`、ColorCode.Core 2.0.15、Mermaider 0.15.1 |

   依赖方向不变：`PuraMarkdown.Extension` → `PuraMarkdown.Agent` → `PuraMarkdown.Core`。

2. **不再有裸 `PuraMarkdown` 包**：Avalonia UI 控件全部归 `PuraMarkdown.Agent`。"PuraMarkdown"继续作为产品名/仓库名使用，但**不再作为包 id 或 C# 根命名空间**。

3. **命名空间随包改名，不保留旧名**（旧命名空间不提供类型转发）：

   | 旧命名空间 | 新命名空间 |
   | --- | --- |
   | `PuraMarkdown.Syntax`、原根命名空间 `PuraMarkdown`（语法层类型） | `PuraMarkdown.Core` |
   | `PuraMarkdown.Security` | `PuraMarkdown.Core.Security` |
   | `PuraMarkdown`、`PuraMarkdown.Rendering`、`PuraMarkdown.Localization`、`PuraMarkdown.Platform` | `PuraMarkdown.Agent`、`PuraMarkdown.Agent.Rendering`、`PuraMarkdown.Agent.Localization`、`PuraMarkdown.Agent.Platform` |
   | `PuraMarkdown.Chat`、`PuraMarkdown.Testing` | `PuraMarkdown.Agent.Chat`、`PuraMarkdown.Agent.Testing` |
   | `PuraMarkdown.Highlighting.ColorCode` | `PuraMarkdown.Extension.Highlighting.ColorCode` |
   | `PuraMarkdown.Diagrams.Mermaid` | `PuraMarkdown.Extension.Diagrams.Mermaid` |

4. **程序集名、项目目录与解决方案同步改名**：`src/PuraMarkdown.Core`、`src/PuraMarkdown.Agent`、`src/PuraMarkdown.Extension`；每个程序集的 `RootNamespace`、`AssemblyName`、`PackageId` 与 PublicAPI baseline 一并更新。
5. **`samples/**`、`tests/**`、`benchmarks/**` 的命名空间保持中性**（`PuraMarkdown.Demo`/`.Spike`/`.Plugins.Samples`/`.Tests`/`.Benchmarks`），不随包改名；`samples/**` 仍 `IsPackable=false`。
6. **文档口径**：当前状态文档（README、CHANGELOG、26/27/28/29/34/35/36、docs 索引、ADR 索引）全部改为新包名与新命名空间；历史里程碑/设计文档（02/04/09/10/11/21/23/24/25、`docs/research/**`）保留原文，只在描述"当前包清单"处加一行改名脚注。

## 理由

- **包名必须表达职责**：`Core` 承诺"基础语法层、无 Avalonia"，`Agent` 承诺"渲染控件 + Agent/Chat"，`Extension` 承诺"官方扩展"，宿主只看包名就能选对引用，不再需要查表。
- **消除"包名 ≠ 命名空间"错位**：一一对应后，`PackageReference` 与 `using` 是同一份心智模型，删掉了"装 A 包、using B 命名空间"的长期解释成本，也避免用户误以为 `using PuraMarkdown.Agent;` 需要安装名为 `PuraMarkdown.Agent` 的另一个包。
- **裸 `PuraMarkdown` 不应被 UI 占用**：产品名与"UI 渲染包"解绑后，"PuraMarkdown 不提供 UI 控件"的口径才可持续；未来若出现纯语法/服务端分发形态，也不会与既有包 id 冲突。
- **`PuraMarkdown.Core` 的无 Avalonia 承诺变得可见**：服务端/无头解析、AOT/trim 与 Arc 类消费方只引 Core 即可，包名本身就是边界（延续 ADR-0025 与 ADR-0027 的硬约束）。
- **改名窗口只有现在**：包从未发布，改动的实际影响面为零；等到 1.0 之后，旧命名空间需要长期保留或走 MAJOR，成本高一个数量级。

## 后果

- **宿主迁移成本（BREAKING）**：必须**同时**修改 `PackageReference` 与 `using`——旧包 id 与旧命名空间都不再保留，不做类型转发、不做 shim。

  | 旧包 | 新包 |
  | --- | --- |
  | `PuraMarkdown.Syntax` | `PuraMarkdown.Core` |
  | `PuraMarkdown` | `PuraMarkdown.Agent` |
  | `PuraMarkdown.Plugins` | `PuraMarkdown.Extension` |

  迁移步骤与 `using` 对照见 [迁移指南](../27-迁移指南-0.1.0-alpha.1.md)；升级策略见 [版本与升级策略](../29-版本与升级策略.md)。
- 公共类型签名本身不变，变的是所在程序集与命名空间；PublicAPI baseline 随程序集迁移（条目数不变：Agent 845 / Core 214 / Extension 22）。
- 依赖图不变，宿主依赖体积不变；`PuraMarkdown.Extension` 仍是唯一可选包，未引用时行为不变（代码高亮回退纯等宽、Mermaid 围栏回退代码块）。
- 包 id 与命名空间一一对应后，文档不再需要"包 → 命名空间映射"来解释错位；27/28 仍保留映射表，只用于迁移。
- 旧包 id 与旧命名空间不可复用；`PuraMarkdown` 不再作为包 id 或根命名空间（如未来出现同类需求必须用新 id/命名空间）。
- 本次仍只做本地 `dotnet pack` 与构建/测试验证，**从未发布到 nuget.org**，文档不得表述为"已发布"。

## 备选

- **只改包 id、保留旧命名空间（即 ADR-0027 的做法再扩展一步）**：被否决。错位依旧存在——`using PuraMarkdown.Agent;` 仍要装另一个 id 的包，文档必须长期维护"包名 ≠ 命名空间"映射，用户误装/误引的概率不减；更关键的是旧命名空间一旦随 1.0 发布就再也删不掉，改名成本会被永久化。既然包从未发布，一次改到位比"先改 id、以后再改命名空间"便宜得多。
- **保留裸 `PuraMarkdown` 作为 meta/兼容包（引用其余三包）**：被否决。从未发布，不存在需要兼容的真实用户；元包只会多出一个"空包"和"PuraMarkdown 是 UI 包"的错误预期，并让"PuraMarkdown 不提供 UI 控件"的口径继续含糊。
- **UI 控件单独发 `PuraMarkdown.Avalonia`，Agent 交互保留 `PuraMarkdown.Agent`**：被否决。Agent/Chat 与控件共用同一渲染栈与同一版本节奏，拆开只增加宿主的版本对齐与引用数量，收益为零（与 ADR-0027 否决 Agent 独立包的理由一致）。
- **只把 `PuraMarkdown.Syntax` 改名为 `PuraMarkdown.Core`，其余两个保持旧名**：被否决。三个包命名风格不一致，`PuraMarkdown` 仍被 UI 占用，`Plugins` 仍与 `IPuraPlugin` 的第三方插件语义混叠。
- **`PuraMarkdown.Plugins` → `PuraMarkdown.Extensions`（复数）**：被否决。与 .NET 生态惯例（`Microsoft.Extensions.*` 是基础设施包而非"扩展插件"）冲突，且本项目的扩展点类型是 `IPuraPlugin`，单数 `Extension` 更能表达"官方扩展实现包"。
- **借机发 `0.2.0-alpha.1` 新版本号**：被否决。包从未发布，版本号变更对用户没有信息量；保持 `0.1.0-alpha.1` 以免文档、tag 与本地包对不上。
