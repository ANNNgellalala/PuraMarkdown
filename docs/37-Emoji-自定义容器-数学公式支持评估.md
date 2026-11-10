# Emoji / 自定义容器 / 数学公式：效果与支持复杂度评估

> 日期：2026-11-09。基线：`PuraMarkdown.Syntax` + `PuraMarkdown` 当前实现（Markdig 1.5.1 / Avalonia 12.1.5 / net10.0），见 [Markdown 功能覆盖与缺口](36-Markdown功能覆盖与缺口.md)。
> 本文最初为评估记录（后附实施状态），工作量按“熟悉本代码库的 1 名开发者”估算，含测试与文档，单位为**人日**，仅用于排期参考。
>
> **实施状态（2026-11-09）**：Emoji 已落地（核心语法开关 + 行内渲染）；数学采用“核心语法 + TeX 文本回退 + 渲染器契约”，CSharpMath 只作为示例插件（`samples/PuraMarkdown.Plugins.Samples`，核心包不依赖），见 ADR-0026；自定义容器按决策暂不做。本文保留为评估记录，具体实现以 `docs/35/36` 与代码为准。

---

## 0. 结论摘要

| 能力 | 用户可见效果 | 主要收益 | 复杂度 | 粗估工作量 | 建议 |
| --- | --- | --- | --- | --- | --- |
| Emoji / Smiley `:smile:`、`:)` | 短代码/表情符号变成 Unicode emoji，和真 emoji 字符一致 | 聊天/Agent 文案更自然；成本最低 | **低** | **0.5–1.5** | 优先做，核心内置；默认只开短代码，smiley 显式开启 |
| 自定义容器 `::: note` | 带左侧色条与标题的提示框（note/tip/warning…），正文可嵌套 Markdown | 文档型内容可读性；与已有 GitHub Alerts 能力重叠 | **低–中** | **2–4** | 按需做；先支持固定 5 类 + 未知类型通用回退 |
| 数学公式 `$...$` / `$$...$$` | 行内/块级 TeX 至少以等宽源码显示；接插件后可真正排版 | Agent/技术内容刚需；排版质量决定体验上限 | **中**（语法+文本回退）<br>**高**（真正排版） | **2–3**（回退）<br>**+5–8**（插件） | 先做核心回退，再做插件；LLM 常用的 `\(...\)` 需额外处理 |

**一句话排序**：Emoji 是“低成本高感知”的顺手项；数学是“高价值但依赖外部引擎”的硬骨头；自定义容器在已有 GitHub Alerts 的前提下，单为 `note`/`tip` 做的边际收益最低，主要价值在**兼容 `:::` 语法的宿主/文档**与**任意名称容器**。

---

## 1. 当前实现基线（工作量从哪来）

### 1.1 语法层：三项都有现成 Markdig 扩展

`PuraSyntaxBuilder` 目前是“按需启用一个 Markdig 扩展 + 一个 builder 方法”的模式（[PuraSyntax.cs](../src/PuraMarkdown.Syntax/PuraSyntax.cs)）。三项能力的语法侧都不需要自研解析器：

| 能力 | Markdig 1.5.1 API | 产出的 AST | 事实 |
| --- | --- | --- | --- |
| Emoji | `UseEmojiAndSmiley(bool enableSmileys = true)` | `EmojiInline : LiteralInline` | 内置 **1603** 个 `:shortcode:` 与 **71** 个 smiley；`Content` 是 Unicode 字符串，`Match` 保留原始写法 |
| 自定义容器 | `UseCustomContainers()` | `CustomContainer : ContainerBlock, IFencedBlock` | `Info`（如 `note`）、`Arguments`（首个空格后的内容）、子块正常解析；支持嵌套（外层围栏须更长） |
| 数学 | `UseMathematics()` | `MathInline : LeafInline`；`MathBlock : FencedCodeBlock` | 行内只解析 `$...$`，块级只解析行首 `$$` 围栏；**不识别** `\(...\)` / `\[...\]` |

> 注意：36 文档此前把数学写成“无 Markdig 数学扩展”，这不准确（本轮已修正该行）：`UseMathematics()`、`UseCustomContainers()`、`UseEmojiAndSmiley()` 在 Markdig 1.5.1 中都存在（可用 `Markdig.xml` 与 `UseAdvancedExtensions()` 的组成核对）。准确表述应是“未启用 + 无默认渲染器/引擎”。

### 1.2 渲染层：默认渲染器是“节点类型 switch + 兜底”

默认行内渲染在 [DefaultInlineRenderer.cs](../src/PuraMarkdown/Rendering/DefaultInlineRenderer.cs)，块级在 [DefaultBlockRenderers.cs](../src/PuraMarkdown/Rendering/DefaultBlockRenderers.cs)、[CodeTableDiagramRenderers.cs](../src/PuraMarkdown/Rendering/CodeTableDiagramRenderers.cs)。与本评估相关的三个事实：

1. **未知行内节点的兜底是输出类型名**：`default` 分支执行 `inline.ToString()`，而 Markdig 的 `Inline`/`MarkdownObject` 没有重写 `ToString()`。因此只启用 `UseMathematics()` 而不处理 `MathInline`，页面会出现 `Markdig.Extensions.Mathematics.MathInline`。
2. **`MathBlock` 会被代码块渲染器接住**：它继承 `FencedCodeBlock` → `CodeBlock`，`CodeBlockRenderer.CanRender` 直接命中，于是显示为等宽 TeX 代码块（可读、可复制，但标题/样式不是数学样式）。
3. **`EmojiInline` 继承 `LiteralInline`**：现有 `case LiteralInline` 已经能显示 emoji 文本，所以 Emoji 的“能否显示”几乎免费；剩下的是字体、字重和开关策略。
4. **`IPuraInlineRenderer` 契约存在但默认渲染器从不调用**（[docs/24](24-插件通用接口与Agent插件化讨论.md) 已记录）。因此行内数学“交给插件渲染”这条路目前走不通，核心必须先补一个默认行内处理或补上 inline 分发。

### 1.3 流式层：扫描器只认识代码围栏

[StreamingBlockScanner.cs](../src/PuraMarkdown.Syntax/StreamingBlockScanner.cs) 目前只跟踪反引号围栏与波浪号围栏，稳定边界=顶层空行且不在围栏内。新增两类“可跨空行的结构”会直接影响流式正确性：

- **未闭合的 `:::` 容器**：Markdig 会把后文全部吞进容器；若扫描器把容器内部空行当稳定边界提交，已提交前缀会被解析成普通文本，后续无法自愈（`Complete` 全量校正才能修正），流式期间会闪错。
- **`$$` 数学块**：块内常有空行（`\begin{aligned}` 等）；同样不能被提前提交，否则尾部窗口从数学块中间开始解析。
- Emoji 是纯行内，**不影响流式边界**。

因此“容器/数学”的隐藏成本主要在扫描器：要让它按已启用特性跟踪 `:::`、`$$`，还要处理嵌套与“特性未启用时不要把 `:::`/`$$` 误当围栏”的兼容性（否则普通文本可能迟迟不提交）。实现上可以让扫描器接收一个特性开关（或由 `PuraSyntaxPipeline` 提供），避免改变未启用这些语法的现有宿主行为。

### 1.4 契约与流程成本（三项都要付）

- [MarkdigCompatibility.cs](../src/PuraMarkdown.Syntax/MarkdigCompatibility.cs) 的节点矩阵与“综合样例只含契约节点”测试；
- 两个 builder 的公开方法要写入 `PublicAPI.Unshipped.txt`（`RS0016/RS0017` 是 error）；
- 本地化（容器标签可复用 Alert 的 `PuraStringKeys`）与主题 token（复用现有 `Accent/Danger/CodeBackground` 即可，无需新颜色）；
- 测试：语法、渲染、流式等价、golden、AOT/裁剪基线（若引入新依赖）；
- 文档同步：35 矩阵、36 覆盖表、README/API 参考、CHANGELOG。

---

## 2. Emoji / Smiley

### 2.1 效果

```markdown
之前：:smile: 收到，马上处理 :) 
之后：😄 收到，马上处理 😃
```

- **短代码 → Unicode**：`:smile:`、`:heart:`、`:fire:`、`:tada:` 等 1603 个 GitHub 风格短代码变成对应 emoji 字符；
- **smiley → emoji（可选）**：`:)`、`:D`、`<3`、`;)` 等 71 个文本表情；
- **自定义 emoji 图片不支持**：Markdig 映射只到 Unicode，不是 `:custom_name:` → 图片；后者需要宿主/插件另行实现（图片 emoji、Twemoji 等不在本方案）；
- 已有的真实 emoji 字符（😄）今天就能显示，此项的净增能力是“**短代码/表情符号的兼容与归一化**”，不是“让 emoji 出现”。

### 2.2 实现要点

1. `PuraSyntaxBuilder.UseEmoji(bool enableSmileys = false)` + `PuraPipelineBuilder.UseEmoji(...)`；内部调用 `UseEmojiAndSmiley(enableSmileys)`。
2. 默认 **关闭 smiley**：smiley 映射里包含 `->`、`=>`、`<=>`、`<-` 等“自定义箭头”（见 `EmojiMapping.GetDefaultSmileyToEmojiShortcode`），开启后正文里的比较/箭头会被替换，属于破坏性行为；短代码默认开启相对安全。
3. 在 `DefaultInlineRenderer.RenderOne` 的 `LiteralInline` 之前增加 `EmojiInline` 分支，用单独 Run（可选复合字体：`Segoe UI Emoji` / `Apple Color Emoji` / `Noto Color Emoji`），**不要设置粗体/斜体**。
4. `RenderEmphasis` 目前会给强调内的所有 Run 设 `FontWeight/FontStyle`；emoji 在 `**:smile:**` 里会被加粗，而 Avalonia 对彩色 emoji 的粗体会退化成黑白（macOS 上还会整份加载彩色字体，内存代价高）。需要跳过 emoji Run（例如内部 `EmojiRun` 标记）。
5. 节点矩阵加入 `EmojiInline`（放在 `LiteralInline` 之前，避免被父类型先匹配），补 35/36 文档。
6. 大纲/复制：`EmojiInline` 走 `LiteralInline` 分支，标题与纯文本复制天然包含 emoji，无需额外处理。

### 2.3 复杂度与风险

| 项 | 评估 |
| --- | --- |
| 语法改动 | 2 个 builder 方法 + 矩阵条目（约 0.1 人日） |
| 渲染改动 | EmojiInline 分支、emoji 字体、强调跳过（约 0.3–0.5 人日） |
| 流式/性能 | 无；解析开销是每个 `:` 的查找，映射表在首次使用时构造一次（可忽略） |
| 测试/文档 | 矩阵、渲染、开关、代码块内不转换、粗体不降级（约 0.5 人日） |
| **合计** | **0.5–1.5 人日** |

风险（中低）：
- **彩色不保证**：Avalonia 12.1.5 的颜色 emoji 渲染依赖字体回退；非 Regular 字重、宿主字体配置、Linux 字体安装都会导致黑白/豆腐块。库能做的是“emoji 单独 Run + 不给它加粗 + 文档建议宿主配置 `FontManagerOptions.FontFallbacks`”，不能保证所有平台彩色。
- 若默认开启 smiley，`->`/`<=` 等文本会被替换，必须作为显式 opt-in。
- Markdig 的 emoji 映射表较大（1603 项 + 前缀树），会带来一次性的构建/内存成本，属于可接受量级。

---

## 3. 自定义容器 `::: note`

### 3.1 效果

```markdown
::: note
这里是说明，支持 **Markdown**。
:::

::: warning 数据可能丢失
删除后不可恢复。
:::
```

- 渲染为**带标题和左侧色条的卡片**；`note/tip/important/warning/caution` 5 类与 GitHub Alerts 的语义/配色/中文标签一致，可直接复用；
- `Arguments`（首个空格后的文本）作为**自定义标题**；正文递归渲染，支持嵌套容器、列表、代码块等；
- 未知类型（`::: my-block`）显示为**通用容器**（标题=名称），保证降级可读；
- 与 `> [!NOTE]` 的区别：只是另一种语法壳；**单看 5 类提示框，边际价值不高**，真正价值在：
  1. 宿主/文档已经在用 `:::`（Docusaurus、VitePress、MkDocs 风格）；
  2. 任意名称容器可作为插件组件的“轻量容器语法”（但交互组件的主路径仍是 ADR-0012 的围栏 + JSON）；
  3. 比引用块更明确的标题/边框语义。

### 3.2 实现要点

1. `UseCustomContainers()` 开关（两个 builder 各一个方法）。
2. 新增 `CustomContainerRenderer`：
   - `Info` 归一化为小写 kind，映射到 Alert 的同款标签与颜色（`PuraStringKeys.AlertNote` 等），未知 kind 用中性色 + Info 原文标题；
   - `Arguments` 只作为**纯文本标题**（不要当 Markdown/HTML 解析，避免注入面）；
   - 子块用 `context.RenderMarkdown(child)` 递归（现有 Quote/Alert 渲染器同款）；
   - 暂不支持通用属性语法（`{#id .class}`）；ADR-0002 已记录 Markdig `HtmlAttributes` 的坑，且本库目前不承诺 HTML 属性。
3. 流式扫描器扩展：跟踪 `:::` 围栏栈（开围栏≥3 个冒号、闭围栏冒号数不少于开围栏、且行尾只有空白），并与既有代码围栏状态共存；特性未启用时保持旧行为（见 §1.3）。
4. 安全/预算：`DefaultAstSanitizer` 的嵌套深度（`MaxNestingDepth`）已经覆盖 `ContainerBlock`；`Arguments` 纯文本显示无注入；HTML 仍禁用。
5. 矩阵加入 `CustomContainer`（Rendered），补文档与测试。
6. 设计边界：只做“展示型容器”，不接管 Agent 交互容器；`:::choice` 仍建议走围栏 + JSON（ADR-0012）。宿主若需要自定义样式，可在默认渲染器之上用 `UseRenderer(priority > 0)` 覆盖 `CustomContainer`。

### 3.3 复杂度与风险

| 项 | 评估 |
| --- | --- |
| 语法改动 | builder 方法 + 矩阵（约 0.2 人日） |
| 渲染改动 | 渲染器 + 5 类映射 + 通用回退（约 1–1.5 人日） |
| 流式改动 | 扫描器围栏栈 + 特性开关 + 流式测试（约 0.5–1 人日） |
| 测试/文档 | 解析/渲染/嵌套/未闭合/未知类型/golden（约 0.5–1 人日） |
| **合计** | **2–4 人日** |

风险（中低）：
- **流式是主要风险**：未闭合容器的“吞后文”语义要求扫描器与尾部窗口配合正确；这是必须补的，不是可选项。
- 与 GitHub Alerts 功能重叠：如果只想要 5 类提示框，已有 `> [!NOTE]` 可以不做；应明确目标宿主是否需要 `:::` 语法。
- 嵌套围栏长度语义（外层须更长）与“未闭合 `:::`”的降级需要在文档里写清。
- 若做成插件：渲染可以插件化（`IPuraRenderer` 可处理任意 Markdig 节点），但扫描器修复仍在核心，插件反而多一层包/契约成本；**建议核心内置**，这也是 36 文档 P2 的定位。

---

## 4. 数学公式

### 4.1 效果

```markdown
行内：质能方程 $E=mc^2$。
块级：
$$
\int_0^1 x^2 \, dx = \frac{1}{3}
$$
```

- **只启用语法、不做渲染**：`MathBlock` 会退化成等宽 TeX 代码块（可读、可复制，但不像公式）；`MathInline` 会输出类型名（不可接受），所以**只要启用 `UseMathematics()`，就必须同时提供行内处理**。
- **核心文本回退**：行内/块级都显示 TeX 源码（等宽、淡背景、可复制），信息不丢失，成本低；这是 [架构 02 §8](../02-architecture.md) 已经约定的降级形态。
- **真正排版（插件）**：接 CSharpMath 后行内/块级渲染为排版好的公式，体验质变；这是数学能力的真正价值所在，也是 1.1 插件候选。
- **LLM 输出兼容**：ChatGPT/Claude 常用 `\(...\)` 与 `\[...\]`，Markdig 的 `UseMathematics()` **不识别**这两种分隔符；不处理的话，Agent 消息里的公式仍会以原文显示。这是“看起来支持了、实际没生效”的最大来源。

### 4.2 分层实现（建议按 A → B → C 推进）

#### A. 核心：语法 + 文本回退（建议先做，2–3 人日）

1. `UseMathematics()` 开关（两个 builder）。
2. `DefaultInlineRenderer` 增加 `MathInline` 分支：显示 `Content` 的 TeX 源码（等宽/淡背景，可选显示 `$` 分隔符以表明是公式）；同时补 `PuraSyntax.AppendInline`、`MarkdownText.AppendInline`，让大纲/复制包含 TeX。
3. 新增 `MathBlockRenderer`（优先级高于 `CodeBlockRenderer`）：等宽 TeX、可选“math”标签与复制按钮，复用 `CodeBlockRenderer` 的 `MaxFenceChars` 预算与截断告警。
4. 流式扫描器识别行首 `$$` 块（与代码围栏同样处理未闭合；`$$` 后不能带信息文本，向 Markdig `MathBlockParser` 对齐），补分块流式等价测试。
5. 矩阵加入 `MathInline`、`MathBlock`（`MathBlock` 条目要在 `FencedCodeBlock` 之前，避免被父类型先匹配）；文档明确“当前为 TeX 源码回退、不排版”。

#### B. 官方插件：真正排版（5–8 人日，建议单独包）

- 候选引擎（2026-11 现状）：
  - `CSharpMath.Avalonia` **1.0.0-pre.2**：MIT，net10.0，依赖 Avalonia ≥ 12.0.4；已提供 Avalonia 12 目标，但**是 prerelease**，AOT/trimming 未验证（据其仓库 issue，内置字体启动时用 Typography 解析 Typeface）。
  - `Sylinko.CSharpMath.Avalonia` **12.0.0**：官方代码的第三方 fork，MIT，net8.0/net10.0，依赖 Avalonia ≥ 12.0.0；版本号稳定，可作为“不接受 prerelease”的备选，但需评估维护活跃度与后续跟进。
  - 其他路线（WPF-Math、KaTeX/MathJax 预渲染 SVG/PNG）在 Avalonia 桌面 + 离线 + AOT 约束下都不如 CSharpMath 直接；预渲染需要宿主提供构建期/服务端渲染能力，不应进核心。
- 插件内容：`PuraMarkdown.Math.CSharpMath`（或使用 fork 的包名）——`IPuraRenderer` 处理 `MathInline`/`MathBlock`，映射为 CSharpMath 的 `MathView`（以包实际 API 为准），配主题字号/颜色、`AutomationProperties.Name`=TeX 源码、复制 TeX；流式尾部（`context.IsTail`）先显示文本回退，提交/完成后再排版，避免每帧重排。
- 包/工程成本：新 csproj + `Directory.Packages.props` + 解决方案/打包脚本 + PublicAPI baseline + AOT/trim 复测 + README/文档/CI，约 1–2 人日，已含在上面的区间里。

#### C. 可选：`\(...\)` / `\[...\]` 归一化（1–2 人日）

- 方案 1（推荐，若确有需求）：自定义 Markdig inline/block parser，识别 `\(...\)`/`\[...\]` 并产出 `MathInline`/`MathBlock`，Span 正确，可与 `$` 共存。
- 方案 2：解析前文本归一化，但会改变偏移，影响 Span/诊断/增量，**不建议在语法层做**；可让宿主/Agent 侧统一输出 `$`。
- 无论哪种，都要补“货币 `$5`/`$10` 不误判”“行内公式跨行不解析”“代码块/链接内不解析”等测试；Markdig 的 `MathInlineParser` 本身有“前言/后语必须是空白或标点”的启发式，能挡住多数货币误判，但不能保证全部。

### 4.3 复杂度与风险

| 方案 | 工作量 | 风险 |
| --- | --- | --- |
| A 核心语法 + 文本回退 | 2–3 人日 | 低；注意 `MathInline` 兜底、扫描器 `$$`、矩阵顺序 |
| A + B 再接 CSharpMath 插件 | 再 +5–8 人日 | **高**：引擎 prerelease/fork 二选一、AOT/trim、字体/启动、TeX 覆盖率（align/矩阵/CJK）、流式重排 |
| A + C 分隔符扩展 | 再 +1–2 人日 | 中；与 `$` 语义、货币误判、Span 一致性的测试成本 |

主要风险清单：
1. **外部引擎**：CSharpMath 官方包目前是 prerelease；AOT/裁剪未验证。若宿主强依赖 Native AOT，需要先做 spike，必要时退化为“文本回退 + 宿主预渲染”。
2. **分隔符不匹配**：LLM 的 `\(...\)`/`\[...\]` 不被 Markdig 识别，不补 C 的话实际收益会打折。
3. **TeX 覆盖**：复杂宏、矩阵、中文混排是 CSharpMath 已知弱项；需要“未知/失败 → TeX 文本回退”的兜底。
4. **流式性能**：公式排版比文本重；必须利用 `IsTail`/提交后渲染 + 缓存，避免每帧重排、避免在 tail 里反复创建控件。
5. **复制/选择/无障碍**：自绘公式不进入文本选择；要提供 TeX 副本、Automation Name，必要时提供源码切换。
6. **文档纠偏**：36 文档该行已修正；后续排期与文档不要再基于“Markdig 没有数学扩展”的错误前提。

---

## 5. 横向对比与建议

| 维度 | Emoji | 自定义容器 | 数学公式 |
| --- | --- | --- | --- |
| 用户感知 | 中（聊天自然度） | 中（提示框/文档味） | 高（技术/Agent 内容） |
| 与现有能力重叠 | 无 | **高**（GitHub Alerts 已覆盖 5 类） | 无（当前无官方渲染） |
| 新增依赖 | 无（Markdig 内置） | 无 | 核心无；插件需 CSharpMath |
| 流式影响 | 无 | 有（`:::` 围栏，必须修） | 有（`$$` 块，必须修） |
| 主要风险 | 彩色 emoji 平台差异、smiley 误替换 | 未闭合容器吞后文、与 Alerts 重复 | 引擎版本/AOT、`\(...\)` 不识别、TeX 覆盖 |
| 建议优先级 | **P1（先做）** | P3（按宿主需求） | **P2（先回退，再插件）** |

**推荐排期（1.1 内容语法）**：

1. **Emoji（0.5–1.5 人日）**：最小改动、最快可见；默认只开短代码，smiley 作为 opt-in。
2. **数学核心回退（2–3 人日）**：先把 `MathInline` 的类型名兜底、`MathBlock` 的 TeX 回退与 `$$` 流式修好；即使暂时不接引擎，也能安全启用语法。
3. **数学插件 spike（1–2 人日评估，决定后再做 5–8 人日）**：先验证 CSharpMath.Avalonia 1.0.0-pre.2 与 Sylinko fork 在 Avalonia 12.1.5 / AOT 下的可用性，再决定是否发布官方包。
4. **自定义容器（2–4 人日）**：若宿主确实需要 `:::` 语法再做；否则继续用 GitHub Alerts，避免维护两套提示框语义。

不建议的做法：
- 为了“有功能”而默认开启 `UseMathematics()`/`UseCustomContainers()`：前者会暴露类型名/改变 `$` 文本语义，后者会改变 `:::` 文本的解析与流式提交，均应 **opt-in**；
- 把 CSharpMath 放进核心（违背 ADR-0014 的零引擎依赖/AOT 目标）；
- 依赖 Markdig GenericAttributes 提供容器属性（已有已知缺陷，且会扩大 HTML/注入面）。

---

## 6. 实施时需同步的契约/文档清单

- `src/PuraMarkdown.Syntax/PuraSyntax.cs`、`src/PuraMarkdown/Pipeline.cs`：新增 `UseEmoji` / `UseCustomContainers` / `UseMathematics`；
- `src/PuraMarkdown.Syntax/MarkdigCompatibility.cs` + `docs/35`：EmojiInline、CustomContainer、MathInline、MathBlock（注意放在父类型之前）；
- `src/PuraMarkdown.Syntax/StreamingBlockScanner.cs`：`:::`/`$$` 围栏与特性开关（保持旧调用行为）；
- `src/PuraMarkdown/Rendering/*`：Emoji 行内处理、容器渲染器、数学行内/块级回退；若补 inline 分发，一并修复 docs/24 的遗留项；
- `src/PuraMarkdown/Localization/PuraLocalization.cs`：容器标签复用 Alert；数学可加“复制公式/TeX 源码”键；
- `PublicAPI.Unshipped.txt`（两个包）；`tests/PuraMarkdown.Tests` 的语法/渲染/流式/golden；`docs/36`、`docs/28`、README、CHANGELOG；引入插件时更新 `docs/29` 包矩阵与 `Directory.Packages.props`；
- 若引入 CSharpMath/fork：重跑 `docs/34` 的 Trim/AOT 基线与性能基线。

---

## 7. 事实来源

- 仓库代码：[PuraSyntax.cs](../src/PuraMarkdown.Syntax/PuraSyntax.cs)、[StreamingBlockScanner.cs](../src/PuraMarkdown.Syntax/StreamingBlockScanner.cs)、[DefaultInlineRenderer.cs](../src/PuraMarkdown/Rendering/DefaultInlineRenderer.cs)、[CodeTableDiagramRenderers.cs](../src/PuraMarkdown/Rendering/CodeTableDiagramRenderers.cs)、[MarkdigCompatibility.cs](../src/PuraMarkdown.Syntax/MarkdigCompatibility.cs)。
- Markdig 1.5.1：本地 NuGet 缓存中的 `Markdig.xml`（`UseEmojiAndSmiley`/`UseCustomContainers`/`UseMathematics`、`EmojiInline`、`CustomContainer`、`MathInline/MathBlock`），以及 [Markdig 源码](https://github.com/xoofx/markdig) 的 `MathInlineParser.cs`、`MathBlockParser.cs`、`EmojiInline.cs`、`EmojiMapping.cs`。
- Avalonia 彩色 emoji/字重问题：Avalonia issues #21124、#16839、#22059（12.1.2 backport）、#21813、#22508（macOS 字体内存）、#18927（macOS 宽度）；12.1.5 release notes 无 emoji 专项修复。
- CSharpMath：NuGet `CSharpMath.Avalonia` 1.0.0-pre.2（MIT，net10.0，Avalonia ≥ 12.0.4，[CSharpMath 仓库](https://github.com/verybadcat/CSharpMath)）；第三方 fork `Sylinko.CSharpMath.Avalonia` 12.0.0（MIT，net8.0/net10.0，Avalonia ≥ 12.0.0，[Sylinko/CSharpMath.Avalonia](https://github.com/Sylinko/CSharpMath.Avalonia)）；AOT/trimming 与 TeX 覆盖率仍需实测。
