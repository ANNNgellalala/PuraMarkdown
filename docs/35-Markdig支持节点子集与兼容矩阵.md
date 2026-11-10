# Markdig 支持节点子集与兼容矩阵（B 阶段）

> 目标：把“B 阶段以 Markdig AST 为语法契约”从口头约定变成**可执行契约**：明确哪些 Markdig 节点属于公共支持范围、由谁渲染、升级 Markdig 时怎么验证。
> 关联：[ADR-0025](adr/0025-syntax-renderer-separation.md)、[B 阶段实施记录](33-B阶段实施记录.md)。

---

## 1. 版本契约

| 项 | 值 |
| --- | --- |
| NuGet 包 | `Markdig` **1.5.1** |
| 程序集版本 | **1.5.0.0** |
| 配置位置 | `Directory.Packages.props`（集中锁定） |
| 运行时检查 | `MarkdigCompatibility.TestedAssemblyVersion` / `CurrentAssemblyVersion` |
| HTML | `DisableHtml()`，不渲染 HTML（ADR-0009） |

`PuraMarkdown.Syntax` 只承诺上表版本；升级 Markdig 必须更新矩阵与基线。

---

## 2. 公共契约的边界

B 阶段的语法契约是 **Markdig `MarkdownDocument`**，但只包括下表中的节点类型。未列入的节点：

- 不承诺语义/行为；
- 默认走文本回退；
- 或由插件实现 `IPuraNodeRenderer`/`IFenceRenderer` 处理。

`PuraDocument.Diagnostics`、`PuraDocument.Outline`、`SourceSpan` 是附带的稳定旁路信息。

---

## 3. 块节点矩阵

| Markdig 节点 | 支持级别 | 默认渲染 | 说明 |
| --- | --- | --- | --- |
| `MarkdownDocument` | 结构 | 不单独出控件 | 根文档 |
| `ParagraphBlock` | 渲染 | 段落 TextBlock | 支持增量更新 |
| `HeadingBlock` | 渲染 | 标题 TextBlock | 支持增量更新；进入大纲 |
| `ThematicBreakBlock` | 渲染 | 分隔线 | — |
| `AlertBlock` | 渲染 | Alert 容器 | 需要 `UseAlertBlocks()` |
| `QuoteBlock` | 渲染 | 引用容器 | 子块递归渲染 |
| `ListBlock` | 渲染 | 列表容器 | 处理有序/无序/任务列表 |
| `ListItemBlock` | 结构 | 由 `ListBlock` 渲染 | 不单独出控件 |
| `MathBlock` | 渲染 | 数学块（默认 TeX 回退） | 需要 `UseMathematics()`；插件可接管排版 |
| `FencedCodeBlock` | 渲染 | 代码块 | `pura-agent` 由 Agent 插件接管 |
| `CodeBlock` | 渲染 | 代码块 | 缩进代码 |
| `Table` | 渲染 | 表格控件 | 需要 `UseGfm()` |
| `TableRow` / `TableCell` | 结构 | 由 `Table` 渲染 | 不单独出控件 |
| `DefinitionList` | 渲染 | 定义列表 | 需要 `UseDefinitionLists()` |
| `DefinitionItem` / `DefinitionTerm` | 结构 | 由 `DefinitionList` 渲染 | 不单独出控件 |
| `FootnoteGroup` | 渲染 | 脚注区 | 需要 `UseFootnotes()` |
| `Footnote` | 结构 | 由 `FootnoteGroup` 渲染 | 不单独出控件 |
| `FootnoteLinkReferenceDefinition` | 解析不渲染 | — | 引用定义组内 |
| `HtmlBlock` | 回退 | 纯文本 | HTML 已禁用 |
| `LinkReferenceDefinitionGroup` | 解析不渲染 | — | 流式期间过滤 |
| `LinkReferenceDefinition` | 解析不渲染 | — | 引用定义参与解析，不显示 |

---

## 4. 行内节点矩阵

| Markdig 节点 | 支持级别 | 默认渲染 | 说明 |
| --- | --- | --- | --- |
| `LiteralInline` | 渲染 | Run | 普通文本 |
| `CodeInline` | 渲染 | Run | 行内代码 |
| `LineBreakInline` | 渲染 | 空格/换行 | 软/硬换行按 CommonMark |
| `EmphasisInline` | 渲染 | Run | 强调/加粗/删除线/上标/下标/插入/高亮 |
| `LinkInline` | 渲染 | Hyperlink/图片 | 链接走 URL 策略与 `LinkClicked` |
| `AutolinkInline` | 渲染 | Hyperlink | 需要 `UseGfm()` |
| `TaskList` | 渲染 | 前缀 Run | 需要 `UseGfm()`；列表项内 |
| `FootnoteLink` | 渲染 | 上标引用/回链 | 需要 `UseFootnotes()` |
| `ContainerInline` | 结构 | 递归子节点 | 强调/链接容器 |
| `MathInline` | 渲染 | 行内公式（默认 TeX 回退） | 需要 `UseMathematics()`；插件可接管 |
| `EmojiInline` | 渲染 | Emoji 文本 | 需要 `UseEmoji()`；继承 `LiteralInline` |
| `HtmlInline` | 回退 | 纯文本 | HTML 已禁用 |

---

## 5. 扩展节点

| 扩展 | 节点 | 条件 | 归属 |
| --- | --- | --- | --- |
| GFM 表格 | `Table`/`TableRow`/`TableCell` | `UseGfm()` | 核心 |
| 任务列表 | `TaskList` | `UseGfm()` | 核心 |
| 自动链接 | `AutolinkInline` | `UseGfm()` | 核心 |
| Agent 围栏 | `FencedCodeBlock`（language=`pura-agent`） | 内置 `PuraMarkdown` 包（命名空间 `PuraMarkdown.Agent`） | Agent 组件 |
| 脚注 | `FootnoteGroup`/`Footnote`/`FootnoteLink` | `UseFootnotes()` | 核心 |
| 定义列表 | `DefinitionList`/`DefinitionItem`/`DefinitionTerm` | `UseDefinitionLists()` | 核心 |
| GitHub Alerts | `AlertBlock` | `UseAlertBlocks()` | 核心 |
| Emoji/Smiley | `EmojiInline` | `UseEmoji()` | 核心 |
| 数学公式 | `MathInline`/`MathBlock` | `UseMathematics()` | 核心语法 + TeX 回退；排版示例见 `samples/PuraMarkdown.Plugins.Samples` |
| Mermaid | `FencedCodeBlock` | 安装 `PuraMarkdown.Plugins`（命名空间 `PuraMarkdown.Diagrams.Mermaid`） | 插件 |
| 自定义 Markdig 扩展 | 插件自定义节点 | 插件注册 | **不在 B 契约** |

自定义扩展节点必须由插件提供 `IPuraNodeRenderer`；否则走 `FallbackTextRenderer`/行内文本回退。

---

## 6. 源位置与 AST 只读约定

- 解析启用 `UsePreciseSourceLocation()`；
- 契约节点保证 `Span`（`Start`/`End`）与 `Block.Line` 可用；
- 宿主/插件**不得修改** AST 节点；需要复制/转换时自行负责；
- 流式增量复用路径会移动旧 AST 节点到新文档（命中时 `previous` 被消费），调用方不应复用旧文档；
- `MarkdownDocument` 由解析器持有，生命周期由调用方管理。

---

## 7. 未知/不支持节点行为

| 情况 | 行为 |
| --- | --- |
| 未知块节点 | `FallbackTextRenderer` 输出纯文本 |
| 未知行内节点 | 默认文本回退（`ToString`/子节点） |
| HTML 块/行内 | 不执行 HTML，按文本回退 |
| 渲染器抛异常 | 该块降级为错误 TextBlock，并触发 warning |
| 无匹配渲染器 | 走 fallback，不崩溃 |

---

## 8. 升级 Markdig 的检查清单

1. 修改 `Directory.Packages.props` 的 `Markdig` 版本；
2. 更新 `MarkdigCompatibility.TestedPackageVersion` / `TestedAssemblyVersion`；
3. 跑 `SyntaxRenderingSeparationTests` 的 `Markdig_version_matches_compatibility_matrix`；
4. 跑 `Comprehensive_document_only_uses_contract_nodes`；
5. 跑全量渲染/golden/流式测试；
6. 复核本矩阵：新增/删除/改名的节点必须回归表格；
7. 更新 CHANGELOG 与 API baseline（若公开类型变化）。

---

## 9. 代码中的可执行契约

```csharp
public static class MarkdigCompatibility
{
    public const string TestedPackageVersion = "1.5.1";
    public const string TestedAssemblyVersion = "1.5.0.0";
    public static IReadOnlyList<PuraNodeSupportInfo> Nodes { get; }
    public static bool IsSupported(MarkdownObject node);
    public static PuraNodeSupportInfo? Describe(MarkdownObject node);
}
```

测试覆盖：

- 版本一致；
- 综合样例产生的所有节点都在矩阵内；
- Table/TaskList 支持级别断言；
- 不支持节点走回退（渲染层既有测试）。

---

## 10. 与 C 阶段的关系

C 阶段的语义模型**只覆盖本矩阵的节点**，不会先承诺 Markdig 全量 AST。  
B 的矩阵是 C 的迁移清单：每个 `Rendered` 节点对应一个语义节点/渲染器迁移项。



