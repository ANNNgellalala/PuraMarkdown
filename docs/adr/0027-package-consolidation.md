# ADR-0027：包整合——7 个 NuGet 包收敛为 3 个

- 状态：已接受（2026-11-10）。其包 id 与命名空间命名由 [ADR-0028](0028-package-identity-rename.md) 更新（`PuraMarkdown.Syntax` → `PuraMarkdown.Core`、`PuraMarkdown` → `PuraMarkdown.Agent`、`PuraMarkdown.Plugins` → `PuraMarkdown.Extension`）；本文的包整合结论与依赖边界不变。
- 关联：ADR-0014（图表/高亮插件契约；本 ADR **保持其有效**，并给出"插件仍外置、但集中到单一插件包"的落地形态）、ADR-0024（Agent 独立包；其"独立包"部分由本 ADR 调整，语法解耦结论不变）、ADR-0023（Mermaider 选型）、ADR-0026（数学公式仍不进官方包）
- 影响文档：[README](../../README.md)、[迁移指南](../27-迁移指南-0.1.0-alpha.1.md)、[公共 API 参考](../28-公共API参考.md)、[版本与升级策略](../29-版本与升级策略.md)、[文档索引](../README.md)、[CHANGELOG](../../CHANGELOG.md)

## 背景

0.1.0-alpha.1 的打包方案按"源码目录"拆成了 7 个 NuGet 包：

`PuraMarkdown.Syntax`、`PuraMarkdown`、`PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing`、`PuraMarkdown.Highlighting.ColorCode`、`PuraMarkdown.Diagrams.Mermaid`。

实际使用暴露的问题：

- **包数与部署边界错位**：Agent/Chat/Testing 与核心始终严格同版本、同发布节奏，拆包只增加宿主的版本对齐负担，没有带来任何独立升级或独立依赖收益。
- **`PuraMarkdown.Testing` 单独成包无价值**：`ControlTreeDumper`/`GoldenFile`/`InteractionProbe`/`StreamingDriver`/`PluginConformance` 只服务于测试宿主，且与核心 API 紧耦合，宿主不得不为"顺便测一下"多引一个包。
- **两个引擎插件包各占一行**：`Highlighting.ColorCode` 与 `Diagrams.Mermaid` 都是可选渲染引擎，用户通常一起启用；拆两个包让引擎依赖分散在两条引用里，文档与升级矩阵也要各维护一行。
- **`PuraMarkdown.Syntax` 必须独立**：它无 Avalonia 依赖，服务 API/服务端、无头解析与 AOT/trim 场景只应拿到语法层，不能被迫拉入整套 Avalonia。
- **时机**：这些包只做过本地 `dotnet pack` 验证，**从未发布到 nuget.org**。包名一旦发布就难以回收，此刻是收敛包 id 的窗口期。

## 决策

1. **官方只发 3 个包**，版本统一保持 `0.1.0-alpha.1`（尚未发布，不升版本、不做兼容层）：

   | 包 | 内容 | 依赖 |
   | --- | --- | --- |
   | `PuraMarkdown.Syntax` | 不变：Markdig 解析、`PuraDocument`、诊断/大纲/扫描；**无 Avalonia** | Markdig 1.5.1 |
   | `PuraMarkdown` | 核心渲染层，并**内置**原 `PuraMarkdown.Agent`（`pura-agent` 围栏、JSON 组件、choice/confirm）、`PuraMarkdown.Chat`（`PuraChatView`、消息级虚拟化）、`PuraMarkdown.Testing`（测试宿主工具） | `PuraMarkdown.Syntax`、Avalonia 12.1.5、Avalonia.Controls.ItemsRepeater 12.0.0、Markdig 1.5.1、JsonSchema.Net 9.6.0 |
   | `PuraMarkdown.Plugins`（新，可选） | 原两个引擎插件合并：`ColorCodeHighlighter`/`ColorCodePlugin` + `MermaidDiagramRenderer`/`MermaidPlugin` | `PuraMarkdown`、ColorCode.Core 2.0.15、Mermaider 0.15.1 |

2. **删除这 5 个包 id**：`PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing`、`PuraMarkdown.Highlighting.ColorCode`、`PuraMarkdown.Diagrams.Mermaid`。
3. **C# 命名空间一律不改名**：`PuraMarkdown.Agent`、`PuraMarkdown.Chat`、`PuraMarkdown.Testing`、`PuraMarkdown.Highlighting.ColorCode`、`PuraMarkdown.Diagrams.Mermaid` 全部保留在新程序集中。公共类型签名不变，**宿主只改 `PackageReference`，不改 `using` 与源码**。
4. **ADR-0014 继续有效**：核心零渲染引擎依赖的原则不变；全部引擎依赖（ColorCode.Core、Mermaider）集中到唯一可选包 `PuraMarkdown.Plugins`。`PuraMarkdown` 与 `PuraMarkdown.Syntax` 不含任何引擎依赖。
5. **`samples/**` 维持 `IsPackable=false`**：Demo/Spike/ArcSample/Plugins.Samples 仍不打包。
6. **文档与索引同步**：当前状态文档（README、27/28/29、docs 索引、ADR 索引、CHANGELOG）改为 3 包口径；历史里程碑/设计文档（02/04/09/10/11/21/23/24/25、research）作为历史记录保留原样。

## 理由

- **包边界应对应依赖边界与部署边界，而不是源码目录**。Agent/Chat/Testing 与核心共享 Avalonia、Markdig、JsonSchema.Net 依赖和发布节奏，合并后依赖图不增加，宿主的版本对齐与升级成本直接下降。
- **保留 Syntax 独立是硬约束**：服务端/无头解析、AOT/trim 与 Arc 类消费方只需要语法层；把 Avalonia 带给它们既违背"语法/渲染分离"（ADR-0025）也污染 trim 基线。
- **引擎依赖必须可"不用就不带"**：ADR-0014 的核心诉求是核心零引擎依赖、插件可替换。把 ColorCode.Core 与 Mermaider 收进单一可选包既满足该约束，又把"装两个包还是三个包"的选择成本降到最低。
- **Testing 合并进核心有正向收益**：宿主写 golden/交互测试时不必再额外引包，测试工具与核心 API 同步演进，避免 Testing 包被迫跟随内部改动。
- **不发布就没有破坏**：5 个包 id 从未上线 nuget.org，删除它们不影响任何真实用户；现在收敛比 1.0 后再收敛便宜得多。

## 后果

- 宿主升级：删除 5 行 `PackageReference`，可选新增 1 行 `PuraMarkdown.Plugins`；`using`/源码不动。迁移清单见 [中文迁移指南](../27-迁移指南-0.1.0-alpha.1.md)。
- 包与命名空间不再一一对应（一个包里有多个命名空间），文档必须显式给出"包 → 命名空间"映射，否则用户会误以为 `using PuraMarkdown.Agent;` 需要装 `PuraMarkdown.Agent` 包。
- 引用 `PuraMarkdown` 的主机会传递引入 `JsonSchema.Net`，即使不使用 `pura-agent` 组件；这是为内置 Agent 组件付出的固定成本（体积小，且 AOT/trim 已验证）。
- 原 Agent/Chat/Testing 三个程序集的公开 API 现在归入 `PuraMarkdown.dll`，PublicAPI baseline 与 API 差异按程序集自然收敛；类型签名本身不变。
- 删除的包 id 不可复用（未来若再拆包必须用新 id）。若出现"只要高亮、不要 Mermaid"的强诉求，可从 `PuraMarkdown.Plugins` 再拆，代价是恢复多包矩阵。
- 未引用 `PuraMarkdown.Plugins` 时行为不变：代码高亮回退纯等宽文本、Mermaid 围栏回退代码块（与 ADR-0014 的降级链一致）。
- 本次仅本地 `dotnet pack` 验证，文档不得表述为"已发布到 nuget.org"；发布动作另行按 `docs/29` 检查单执行。

## 备选

- **只发 1 个包（把 Syntax 与 Plugins 也合入 `PuraMarkdown`）**：被否决，两个不可接受的原因——(a) 会把 Avalonia 依赖带给服务端/无头/控制台解析场景，破坏 `PuraMarkdown.Syntax` 的无 Avalonia 承诺与 AOT/trim 基线；(b) 会把 ColorCode.Core/Mermaider 带回核心包，与 ADR-0014"核心零引擎依赖"直接冲突，用户"不用也要带"，且无法替换引擎。
- **保持 7 个包**：被否决。0.1.0-alpha.1 尚未发布，正是收敛时机；继续拆分只增加版本对齐、升级步骤与文档矩阵的维护成本，收益为零。
- **4 个包（`Plugins` 仍拆成高亮与 Mermaid 两个）**：技术上可保留"只装一个引擎"的最小依赖，但两个引擎互不相关、用户几乎总是一起启用，拆开只是把选择成本转移给用户；等出现明确只装其一的诉求再拆不迟。
- **把 Testing 单独保留成包**：被否决，生产集成中无价值，且与核心同步版本，独立包只带来引用负担。
- **借机发 0.2.0-alpha.1 新版本号**：被否决，包从未发布，版本号变更对用户没有信息量；保持 `0.1.0-alpha.1` 以免文档、tag 与本地包对不上。
