# Markdig 扩展点与流式解析技术调研

> 基线：Markdig **1.5.1**（最新稳定版，1.0 发布于 2026-02-22）。API 经反射探针（.NET 8 环境实测）、仓库源码与官方文档核对；0.3x/0.4x 差异见 §7，未实测项标「待验证」。
>
> **决策后注记（2026-11-05，v0.3）**：本文的实测结论（无增量解析、`UsePreciseSourceLocation` 必开、`DisableHtml` 不过滤 URL scheme、CustomContainer 语义、性能基线）在 v0.3 全部有效。仅"IR 转换"相关建议已过时：v0.3 **不再自建 IR**，渲染器直接消费 Markdig AST（ADR-0011），因此 §3/§8 中"生成 IR"的表述应读作"遍历 AST 生成 Avalonia 控件"。

## 0. 结论

## 1. 管道构建与扩展点
`MarkdownPipelineBuilder` 可变、**非线程安全**：`BlockParsers`/`InlineParsers`/`Extensions`、`PreciseSourceLocation`、`TrackTrivia`、`MaximumNestingDepth`（默认 128）。`MarkdownPipeline` 不可变、线程安全、应复用。入口 `Markdown.Parse(string, MarkdownPipeline?, MarkdownParserContext?)`、`Markdown.ToHtml(...)`；**`MarkdownParser` 类在 1.5.1 已不存在**。
`UseAdvancedExtensions()` 实测包含：`AlertBlocks, Abbreviations, AutoIdentifiers, Citations, CustomContainers, DefinitionLists, EmphasisExtras, Figures, Footers, Footnotes, GridTables, Mathematics, MediaLinks, PipeTables, ListExtras, TaskLists, Diagrams, AutoLinks, GenericAttributes`（最后者**必须最后**）。**不含** Emoji、SmartyPants、YamlFrontMatter、PreciseSourceLocation、TrackTrivia、DisableHtml（均需显式追加）。
```csharp
public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions().UseEmojiAndSmiley().UsePreciseSourceLocation().Build();
public sealed class MyRuleExtension : IMarkdownExtension        // 仅两个 Setup
{
    public void Setup(MarkdownPipelineBuilder p)                  // 建管道阶段：改 parser 列表
    {
        if (!p.BlockParsers.Contains<MyBlockParser>()) p.BlockParsers.Insert(0, new MyBlockParser());
        if (!p.InlineParsers.Contains<MyInlineParser>()) p.InlineParsers.InsertBefore<LinkInlineParser>(new MyInlineParser());
    }
    public void Setup(MarkdownPipeline p, IMarkdownRenderer r)    // 渲染阶段：注册对象渲染器
    {
        if (r is HtmlRenderer h) h.ObjectRenderers.Insert(0, new HtmlMyRenderer());
    }
}
```

## 2. 自定义语法
**类型事实（实测）**：无 `BlockParser<T>`/`InlineParser<T>`；泛型基类仅 `FencedBlockParserBase<T>`；`BlockParser` 覆写 `TryOpen`/`TryContinue`/`Close`，返回 `BlockState`（`None/Skip/Continue/ContinueDiscard/Break/BreakDiscard`）；`InlineParser` 唯一方法 `Match(InlineProcessor, ref StringSlice)`；`BlockProcessor` 提供 `Line`、`IsCodeIndent`、`IsBlankLine`、`LineIndex`、`Column`、`Start`、`ColumnBeforeIndent`、`NewBlocks`、`GoToColumn`、`Open/Close/Discard`、`TakeLinesBefore`、`TrackTrivia`。
```csharp
public sealed class FenceBlock : ContainerBlock        // %%% 容器；::: 也可直接继承 FencedBlockParserBase<T>
{
    public FenceBlock(BlockParser parser) : base(parser) { }
    public char FencedChar { get; set; }
    public int OpeningFencedCharCount { get; set; }
    public int ClosingFencedCharCount { get; set; }
}
public sealed class FenceBlockParser : BlockParser
{
    public FenceBlockParser() { OpeningCharacters = new[] { '%' }; }
    public override BlockState TryOpen(BlockProcessor p)
    {
        if (p.IsCodeIndent) return BlockState.None;
        var line = p.Line; char c = line.CurrentChar; int n = 0;
        while (line.CurrentChar == c) { n++; line.SkipChar(); }
        if (n < 3) return BlockState.None;
        p.NewBlocks.Push(new FenceBlock(this) { FencedChar = c, OpeningFencedCharCount = n,
            Line = p.LineIndex, Column = p.Column, Span = new SourceSpan(p.Start, p.Line.End) });
        return BlockState.ContinueDiscard;       // 首行已消费，内容按普通块解析
    }
    public override BlockState TryContinue(BlockProcessor p, Block block)
    {
        var fence = (FenceBlock)block; var line = p.Line; int closing = 0;
        while (line.CurrentChar == fence.FencedChar) { closing++; line.SkipChar(); }
        if (closing >= fence.OpeningFencedCharCount && !p.IsCodeIndent)
        { fence.ClosingFencedCharCount = closing; return BlockState.BreakDiscard; }
        p.GoToColumn(p.ColumnBeforeIndent);      // 子块从当前缩进继续
        return BlockState.Continue;
    }
    public override bool Close(BlockProcessor p, Block block)
    {
        if (((FenceBlock)block).ClosingFencedCharCount == 0) block.SetData("unclosed", true);
        return true;                             // 返回 false 会从 AST 删除
    }
}
```

**`:::` 落 AST 与属性（实测）**：`CustomContainer : ContainerBlock, IFencedBlock`：`Info`、`Arguments`、`FencedChar`、`Opening/ClosingFencedCharCount`、`Span/Line/Column`，子块正常解析。属性：`HtmlAttributesExtensions.TryGetAttributes()/GetAttributes()`（`Markdig.Renderers.Html`），`HtmlAttributes` 有 `Id`、`Classes`、`Properties`、`AddClass`。实测：`::: warning` → Info 当 class；`::: {.alert #my role="alert"}` → 属性全输出（推荐）；`::: x {.y}` → `.y` 被吞；嵌套须 `::::` 套 `:::`。
```csharp
public sealed class KeyboardInline : LeafInline { public string? Shortcut { get; set; } }
public sealed class KeyboardInlineParser : InlineParser     // 成对分隔符可改用 EmphasisDescriptor
{
    public KeyboardInlineParser() { OpeningCharacters = new[] { '[' }; }
    public override bool Match(InlineProcessor p, ref StringSlice slice)
    {
        if (slice.CurrentChar != '[' || slice.PeekChar(1) != '[') return false;
        int start = slice.Start, close = slice.Text.IndexOf("]]", start + 2, StringComparison.Ordinal);
        if (close < 0) return false;                        // 不匹配不得推进 slice
        p.Inline = new KeyboardInline { Shortcut = slice.Text.Substring(start + 2, close - start - 2),
            Span = new SourceSpan(p.GetSourcePosition(start, out int line, out int col),
                                  p.GetSourcePosition(close + 1, out _, out _)), Line = line, Column = col };
        slice.Start = close + 2;
        return true;
    }
}
```

## 3. 渲染与「UI 无关 IR」
`IMarkdownRenderer` → `RendererBase`（`Write`/`WriteChildren`）→ `TextRendererBase<TRenderer>` → `HtmlRenderer`（`EnableHtmlForBlock/Inline`、`BaseUrl`、`WriteEscape`、`WriteEscapeUrl`、`WriteAttributes`）；自定义 `HtmlObjectRenderer<TObject>` 覆写 `protected override void Write(HtmlRenderer, TObject)`；`ToHtml` 内部池化 renderer（`RentHtmlRenderer` internal）。
**推荐直接遍历 AST 生成 IR**：`Descendants()`/`Descendants<T>()`（`Markdig.Syntax.MarkdownObjectExtensions`，DFS 同出 Block 与 Inline）；无 `MarkdownObjectVisitor`/`DocumentWalker`。`ContainerBlock` 是 `IList<Block>`；`ContainerInline` 用 `FirstChild/LastChild/PreviousSibling/NextSibling`；`LeafBlock.Lines` 是 public field（`Lines.Lines[i].Slice`）。
**位置信息**：`MarkdownObject.Span` 是 public **field**（`SourceSpan`，`Start/End` inclusive 偏移），`Line/Column` 为 0-based；默认 pipeline 下 inline span 全为 `(0,0,L0,C0)`，**必须 `UsePreciseSourceLocation()`**（实测 `# H1 *em*`：EmphasisInline 默认 `(0,0,L0,C0)`，开启后 `(5,8,L0,C5)`）。`Remove()/ReplaceBy()` 不自动重算 Span。
```csharp
IrBlock Convert(ContainerBlock container, int offset)    // Span 保留并平移（流式切片用）
{
    var node = new IrBlock { Kind = container.GetType().Name, Line = container.Line,
        Start = container.Span.Start + offset, End = container.Span.End + offset };
    foreach (var b in container)
        node.Children.Add(b switch {
            HeadingBlock h => new IrBlock { Kind = "heading", Level = h.Level, Inlines = Inlines(h.Inline, offset) },
            ParagraphBlock p => new IrBlock { Kind = "paragraph", Inlines = Inlines(p.Inline, offset) },
            FencedCodeBlock f => new IrBlock { Kind = "code", Language = f.Info, Text = GetLines(f) },
            ContainerBlock c => Convert(c, offset),
            _ => new IrBlock { Kind = b.GetType().Name } });
    return node;
}
// inline 层：LeafBlock.Inline 起按 FirstChild/NextSibling 遍历，映射为 text/strong/em/code/link/image，
// Span 同样平移 offset；尾部不稳定块整块替换。
```

## 4. 流式解析方案
**能否增量：否。** ① 入口只接受完整 `string`（issue #489 官方答复无 Stream 解析）；② issue #858（2025-03，仍 open）作者回复「可行但 non-trivial、欢迎 PR」，至今未合入（1.5.1 无 `CompleteOpenElements`）；③ 无「未完成→继续解析」API。
**社区做法**：解析后修补 AST（JamesNK `CompletePartialElements`：补全未闭合 code/emphasis、删未闭合链接；MihaZupan 版用 `EmbraceChildrenBy`），属渲染期补全而非增量。
**建议：边界切分 + 尾部窗口重解析 + 稳定块提交。** scanner 维护 fence 状态、缩进代码、HTML block type、列表/引用栈；可提交边界 = 顶层且 `fenceDepth==0` + 空行（或 EOF 强制 flush）。表格须含「表头 + `|---|` 分隔行」才整块提交（实测缺分隔行退化为段落）；引用/脚注定义**文档级非单调**：每帧预扫描 definitions 或延迟提交，已提交块不回溯。尾窗口 8–16 KB，超限强制提交。
```text
state: buffer; committedOffset=0; committedIr; pipeline
onChunk(chunk):
  buffer.Append(chunk)
  cut = ScanBlockBoundaries(buffer, from: committedOffset).LastOrDefault(c => buffer.Length - c <= MaxTailChars)
  if cut == 0: return                        // 尾部还不稳定
  doc = Markdown.Parse(buffer.ToString(cut, buffer.Length - cut), pipeline)   // 只解析尾窗口
  ir  = Convert(doc, offset: cut); stable = 0
  foreach (b in ir.Children)
      if (b.End < buffer.Length - 1 && !DependsOnLaterDefinitions(b)) stable++ else break
  Commit(ir.Children.Take(stable)); committedOffset = ir.Children[stable-1].End + 1
  Emit(delta: committed 追加 + tail 整块替换)
onStreamEnd(): CompletePartialElements(Markdown.Parse(buffer.ToString(committedOffset), pipeline)); CommitAll()
```

## 5. 安全

- `DisableHtml()`：移除 `HtmlBlockParser` + `AutolinkInlineParser.Options.EnableHtmlParsing=false`；实测原始 HTML 全部转义，但源码明确 **"does not sanitize the generated HTML"**。
- **URL scheme 不过滤（实测）**：`[x](javascript:alert(1))`、`JaVaScRiPt:`、`data:text/html;base64,...` 均原样进 `href`；`WriteEscapeUrl` 只做转义/IDN/非 ASCII/`BaseUrl`，无协议白名单。→ IR 层做 allowlist（`http/https/mailto/tel` + 相对路径），并加 `rel="noopener noreferrer"`。
- CommonMark：通过 0.31.2 的 600+ 测试（1.5.1 约 7065 个）；1.4.0 的「空白行不结束 HTML block」在 1.5.1 已修复；仍有零散 open issue（如 #423）。
- 健壮性：空输入→0 block；`MaximumNestingDepth` 默认 128（流式建议下调 32–64）；**BOM 不剥离**（实测 `"\uFEFF# h"` → 段落，首标题丢失），CRLF 正常。

## 6. 性能
`MarkdownPipeline` 不可变、线程安全（源码注释 + 并行实测）；builder、`HtmlRenderer`、`Processor` 不可跨线程共享。节点口径 `Descendants().Count()`；分块解析几乎不省总成本（−25%），价值在首屏与增量。基线：单块 ≤0.1 ms；8–16 KB 尾窗口重解析 ≤1 ms；100 KB 整篇 ≤50 ms；内存 ≤10× 输入；每帧最多 1 次重解析。优化：免 LINQ/反射、`sealed`、`Span`、`ArrayPool`、避免 HTML 中间产物。

## 7. 已知坑
1. `UseGenericAttributes()` 必须最后注册；顺序错误会让 `{...}` 全部失效。
2. GenericAttributes 生效范围窄：标题/段落/链接/图片/fenced code info/容器首行有效；**emphasis、code span、纯文本、list item、blockquote 上的 `{...}` 不解析且原样输出**（1.5.1 实测，文档表述对应 main，待验证）。
3. **单字符 class 解析失败（实测 bug）**：`## H {#sec .c}` → 属性不解析、标题残留 `{#sec .c}`；`.cc`/`.c1` 正常。
4. 容器语义：Info 被当 class；`::: x {.y}` 的 `.y` 被吞且不覆盖 Info class；语义统一走 `{...}`。
5. 嵌套 `:::` 外层围栏须更长（`::::` 套 `:::`），否则内层提前关闭外层。
6. 未闭合：fence 安全（隐式闭合）；`:::` 会把后文全部吞入容器；inline partial 显示为字面量。
7. 表格必须有分隔行：`| a | b |\n| 1 | 2 |` 实测退化为 `<p>`；切在表中间会整表跳动。
8. 引用/脚注定义跨 chunk 不回填（全文解析正常，实测 `text[^1]` 定义在后也可解析）。
9. 1.x breaking：1.0 移除 obsolete API（PR #926）；1.5.1 中 `MarkdownParser` 不存在、`Span`/`LeafBlock.Lines` 是 field、无泛型 parser 基类；0.45→1.x 差异以编译错误为准（待验证）。
10. 文档超前于发布：官方文档对应 main，`DataKey<T>`/`MarkdownObjectDataExtensions`/`CompleteOpenElements` 未在 1.5.1 确认（待验证）。
11. TFM：1.x = `net462;netstandard2.0;netstandard2.1;net8.0;net10.0`；0.45 = `…;net8.0;net9.0`，`netstandard2.0` 依赖 `System.Memory`。

## 8. 结论与建议
1. 锁定 Markdig 1.5.1（或 1.x 最新补丁），不要照 0.3x 教程写代码。
2. 进程级单例 `MarkdownPipeline`（advanced + Emoji + `UsePreciseSourceLocation`），严禁每帧 `Build()`。
3. 不走 HTML 字符串做 UI；实现独立 AST→IR 转换器，与 Markdig 类型解耦。
4. 强制 `UsePreciseSourceLocation()`，IR 保存 `Span/Line/Column` 作为稳定身份与增量键。
5. 遍历统一用 `Descendants()`/`Descendants<T>()`，勿自写递归。
6. 容器语义只认 `::: {.kind #id}`，`Info`/`Arguments` 仅辅助；不要生成单字符 class。
7. 流式用「边界扫描 + 尾部窗口重解析 + 稳定块提交」：提交条件为顶层空行且 `fenceDepth==0`，尾窗口 8–16 KB。
8. 展示层单独处理 partial inline（补全或延迟一帧）；fence 未闭合交给隐式闭合。
9. 表格、引用/脚注定义视为不稳定块：表格含分隔行整块提交，definitions 预扫描或延迟提交。
10. IR 层兜底：URL scheme 白名单 + `rel="noopener noreferrer"` + 不渲染原始 HTML。
11. 输入规范化：去 BOM、统一 LF、深度限制 32–64、限制单块与总长。
12. 建立可测基线（ms/KB、alloc/KB、nodes/KB、尾窗口耗时、首屏），每帧最多 1 次重解析，超预算降级纯文本；CI 跑 CommonMark spec + 黄金样例。

## 9. 参考

- 仓库/文档/API：<https://github.com/xoofx/markdig>、<https://xoofx.github.io/markdig/docs/>、<https://xoofx.github.io/markdig/api/>
- 流式：#858 <https://github.com/xoofx/markdig/issues/858>；JamesNK gist <https://gist.github.com/JamesNK/4d4fbef86cdde2ab54df8bae8421bed6>；#489 <https://github.com/xoofx/markdig/issues/489>
