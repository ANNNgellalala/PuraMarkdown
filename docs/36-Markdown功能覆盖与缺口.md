# Markdown 功能覆盖与缺口（当前实现）

> 日期：2026-11-08。以当前 `PuraMarkdown.Syntax` + 默认渲染器为准；历史检查见 [20-Markdown完整性检查](20-Markdown完整性检查.md)。
> 节点契约见 [Markdig 支持节点子集与兼容矩阵](35-Markdig支持节点子集与兼容矩阵.md)。

---

## 1. 已支持

### 1.1 CommonMark 核心

| 能力 | 状态 |
| --- | --- |
| 段落、ATX/Setext 标题 | ✅ |
| 引用块、嵌套引用 | ✅ |
| 有序/无序列表、嵌套列表、紧凑/宽松列表 | ✅ |
| 缩进代码块、围栏代码块 | ✅ |
| 分隔线 | ✅ |
| 强调/加粗 | ✅ |
| 软换行（空格）/硬换行 | ✅ |
| 链接、图片、行内代码 | ✅ |
| 链接引用定义（含流式跨窗口） | ✅ |
| 转义字符、HTML 实体 | ✅（Markdig 解析） |
| 自动链接 | ✅（GFM） |

### 1.2 GFM

| 能力 | 状态 |
| --- | --- |
| 管道表格（列对齐、缺列补齐、表头样式） | ✅ |
| 任务列表（只读 CheckBox/符号） | ✅ |
| 删除线 `~~text~~` | ✅ |
| 自动链接 | ✅（可点击、走 URL 策略） |

### 1.3 插件/扩展

| 能力 | 状态 |
| --- | --- |
| `pura-agent` 围栏 + choice/confirm | ✅（Agent 包） |
| Mermaid | ✅（Mermaid ASCII 插件） |
| ColorCode 语法高亮 | ✅（插件） |
| 自定义围栏/行内/块渲染 | ✅（`IFenceRenderer`/`IPuraInlineRenderer`/`IPuraNodeRenderer`） |
| 自定义 Markdig 扩展节点 | ⚠️ 可解析；需插件提供 renderer，否则文本回退 |

---

## 2. 未实现/部分实现

### 2.1 语法扩展（未启用）

| 功能 | 状态 | 说明 |
| --- | --- | --- |
| 脚注 `[^1]` | ❌ 未启用 | Markdig `UseFootnotes` 未加入管道；无 footnote 节点渲染 |
| 定义列表 | ❌ 未启用 | `UseDefinitionLists` 未加入 |
| 缩写 `*[HTML]: ...` | ❌ 未启用 | `UseAbbreviations` 未加入 |
| Emoji/Smiley `:smile:` | ❌ 未启用 | `UseEmojiAndSmiley` 未加入 |
| 自定义容器 `::: note` | ❌ 未启用 | `UseCustomContainers` 未加入；当前 Agent 只支持围栏 |
| YAML front matter | ❌ 未启用 | `UseYamlFrontMatter` 未加入；`---` 会按普通 Markdown 解析 |
| 数学公式 `$...$`/`$$...$$` | ❌ 未启用 | 无 Markdig 数学扩展 + 无官方渲染器插件 |
| 引用/文献 `[@key]` | ❌ 未启用 | `UseCitations` 未加入 |
| 自动标题 ID | ❌ 未启用 | 有 `PuraOutline` 锚点，但控件没有 HTML id/permalink |
| 通用属性 `{#id .class}` | ❌ 未启用 | `UseGenericAttributes` 未加入 |
| GitHub Alerts `> [!NOTE]` | ❌ 未启用 | 无对应扩展/渲染器；目前显示为普通引用 |

### 2.2 Emphasis Extras（部分）

管道调用了 `UseEmphasisExtras()`，但渲染器目前只完整处理 **删除线**：

| 语法 | 期望 | 当前 |
| --- | --- | --- |
| `~~strike~~` | 删除线 | ✅ |
| `~sub~` | 下标 | ❌ 按删除线处理 |
| `^sup^` | 上标 | ❌ 按斜体处理 |
| `++insert++` | 插入（下划线） | ❌ 按加粗处理 |
| `==mark==` | 高亮 | ❌ 按加粗处理 |

> 建议：要么关闭未实现的 EmphasisExtras 选项，要么补齐 `EmphasisInline.DelimiterChar` 语义。

### 2.3 渲染/交互能力

| 功能 | 状态 | 说明 |
| --- | --- | --- |
| 渲染 TOC / 标题永久链接 | ❌ | 只有 `GetOutline()`/`ScrollToAnchor()` API，无 TOC 控件或标题锚链接 |
| 脚注 UI/回链 | ❌ | 语法未启用 |
| 自定义容器样式 | ❌ | 语法未启用 |
| 代码行号 | ❌ | 有语言标签/复制，无行号 |
| 代码行高亮/diff | ❌ | 无对应语法或渲染 |
| 代码块标题/文件名 | ❌ | 没有独立语法约定 |
| 图片尺寸/对齐语法 | ❌ | 仅 URL + alt；尺寸由宿主 loader/控件决定 |
| 任务列表交互 | ❌ | 只读展示（设计上只读 Markdown） |
| HTML 渲染 | ❌（设计禁用） | ADR-0009；HTML 文本回退 |
| 图表（非 Mermaid） | ❌ | 只有 Mermaid 插件；无 ScottPlot/`pura-chart` 官方插件 |
| Mermaid 图形化 | ⚠️ | 当前为 ASCII/文本渲染；不是图形画布 |

### 2.4 解析/规范层面

| 项 | 状态 |
| --- | --- |
| CommonMark 全量 spec 套件 | ❌ 未接入；当前为精选测试 + golden |
| GFM 全量 spec | ❌ 未接入 |
| Markdig Advanced Extensions 全量 | ❌ 只按需启用 |
| 多语言/双向文本专项 | ⚠️ 依赖 Avalonia/字体，未专项验证 |

---

## 3. 设计上有意不做

| 项 | 原因 |
| --- | --- |
| HTML 渲染 | 安全策略（ADR-0009），按文本回退 |
| 交互式任务列表 | Markdown 控件只读；交互由宿主/Agent 组件负责 |
| 图表/数学引擎内置 | 插件化（ADR-0014）；核心不引入引擎依赖 |
| 完整 CommonMark spec 套件 | 目前以测试/golden 覆盖主要路径，后续可作为质量增强 |

---

## 4. 建议优先级

### P1：低成本、常见 Markdown 兼容

1. 修正 EmphasisExtras 语义（`^`/单`~`/`++`/`==`），或关闭未支持选项；
2. 脚注（`UseFootnotes` + Footnote 节点渲染 + 回链）；
3. 定义列表（`UseDefinitionLists` + 渲染器）；
4. GitHub Alerts（`> [!NOTE]`）识别为引用扩展；
5. YAML front matter（`UseYamlFrontMatter`，默认不渲染或折叠）。

### P2：内容生态

6. 数学公式插件（Markdig 数学扩展 + 渲染器，如 CSharpMath）；
7. 自定义容器（`::: note`）+ 样式；
8. 自动标题 ID + TOC/permalink；
9. 代码行号/行高亮/diff；
10. Emoji/缩写/引用等扩展。

### P3：质量

11. CommonMark/GFM spec 子集测试；
12. 双向文本/复杂字体手工走查。

---

## 5. 新增扩展的接入方式

```csharp
var syntax = new PuraSyntaxBuilder()
    .UseCommonMark()
    .UseGfm()
    .UseMarkdownExtension(new MyMarkdigExtension())
    .Build();

var pipeline = new PuraPipelineBuilder()
    .UseCommonMark()
    .UseGfm()
    .UseMarkdownExtension(new MyMarkdigExtension())
    .UseRenderer(new MyNodeRenderer())
    .Build();
```

新增节点必须：

- 更新 [Markdig 节点矩阵](35-Markdig支持节点子集与兼容矩阵.md)；
- 补 `PuraNodeSupportInfo`（`Rendered/Structural/Fallback`）；
- 提供 renderer 或明确 fallback；
- 加语法/渲染/流式测试。

---

## 6. 当前验证

```
dotnet build PuraMarkdown.slnx        → 0 警告 / 0 错误
dotnet test tests/PuraMarkdown.Tests  → 114 通过 / 0 失败
```
