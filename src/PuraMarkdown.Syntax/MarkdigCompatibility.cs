using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PuraMarkdown.Syntax;

/// <summary>节点在 B 阶段公共契约中的支持级别。</summary>
public enum PuraNodeSupport
{
    /// <summary>由默认渲染器渲染为控件。</summary>
    Rendered,

    /// <summary>结构节点（由父节点渲染，不单独出控件）。</summary>
    Structural,

    /// <summary>解析但默认不渲染（如引用定义）。</summary>
    ParsedNotRendered,

    /// <summary>仅作为文本/HTML 回退展示。</summary>
    Fallback,

    /// <summary>不属于 B 契约；插件可自行处理。</summary>
    NotContract,
}

/// <summary>一条 Markdig 节点兼容性说明。</summary>
public sealed record PuraNodeSupportInfo(
    Type NodeType,
    PuraNodeSupport Support,
    string Notes);

/// <summary>
/// B 阶段的 Markdig 支持节点子集与兼容矩阵。
/// MarkdownDocument 是语法契约；只有这里列出的节点类型属于公共支持范围。
/// </summary>
public static class MarkdigCompatibility
{
    /// <summary>已测试的 Markdig NuGet 包版本。</summary>
    public const string TestedPackageVersion = "1.5.1";

    /// <summary>Markdig 1.5.1 的程序集版本（用于 CI 回归提醒）。</summary>
    public const string TestedAssemblyVersion = "1.5.0.0";

    /// <summary>当前运行时 Markdig 程序集版本。</summary>
    public static string CurrentAssemblyVersion
        => typeof(Markdown).Assembly.GetName().Version?.ToString() ?? string.Empty;

    /// <summary>契约内节点及支持级别（按具体类型优先排列）。</summary>
    public static IReadOnlyList<PuraNodeSupportInfo> Nodes { get; } =
    [
        new(typeof(MarkdownDocument), PuraNodeSupport.Structural, "根文档"),
        new(typeof(ParagraphBlock), PuraNodeSupport.Rendered, "段落"),
        new(typeof(HeadingBlock), PuraNodeSupport.Rendered, "ATX/Setext 标题"),
        new(typeof(ThematicBreakBlock), PuraNodeSupport.Rendered, "分隔线"),
        new(typeof(AlertBlock), PuraNodeSupport.Rendered, "GitHub Alerts（UseAlertBlocks）"),
        new(typeof(QuoteBlock), PuraNodeSupport.Rendered, "引用"),
        new(typeof(ListBlock), PuraNodeSupport.Rendered, "有序/无序列表"),
        new(typeof(ListItemBlock), PuraNodeSupport.Structural, "列表项；由 ListBlock 渲染"),
        new(typeof(FencedCodeBlock), PuraNodeSupport.Rendered, "围栏代码块（含 pura-agent）"),
        new(typeof(CodeBlock), PuraNodeSupport.Rendered, "缩进代码块"),
        new(typeof(Table), PuraNodeSupport.Rendered, "GFM 表格（UseGfm）"),
        new(typeof(TableRow), PuraNodeSupport.Structural, "表格行；由 Table 渲染"),
        new(typeof(TableCell), PuraNodeSupport.Structural, "表格单元格；由 Table 渲染"),
        new(typeof(DefinitionList), PuraNodeSupport.Rendered, "定义列表（UseDefinitionLists）"),
        new(typeof(DefinitionItem), PuraNodeSupport.Structural, "定义项；由 DefinitionList 渲染"),
        new(typeof(DefinitionTerm), PuraNodeSupport.Structural, "定义术语；由 DefinitionList 渲染"),
        new(typeof(FootnoteGroup), PuraNodeSupport.Rendered, "脚注区（UseFootnotes）"),
        new(typeof(Footnote), PuraNodeSupport.Structural, "单个脚注；由 FootnoteGroup 渲染"),
        new(typeof(FootnoteLinkReferenceDefinition), PuraNodeSupport.ParsedNotRendered, "脚注引用定义；不渲染"),
        new(typeof(HtmlBlock), PuraNodeSupport.Fallback, "HTML 已禁用；按文本回退"),
        new(typeof(LinkReferenceDefinitionGroup), PuraNodeSupport.ParsedNotRendered, "引用定义组；不渲染"),
        new(typeof(LinkReferenceDefinition), PuraNodeSupport.ParsedNotRendered, "引用定义；不渲染"),
        new(typeof(LiteralInline), PuraNodeSupport.Rendered, "普通文本"),
        new(typeof(CodeInline), PuraNodeSupport.Rendered, "行内代码"),
        new(typeof(LineBreakInline), PuraNodeSupport.Rendered, "软/硬换行"),
        new(typeof(EmphasisInline), PuraNodeSupport.Rendered, "强调/加粗/删除线/上下标等"),
        new(typeof(LinkInline), PuraNodeSupport.Rendered, "链接/图片"),
        new(typeof(AutolinkInline), PuraNodeSupport.Rendered, "自动链接（UseGfm）"),
        new(typeof(TaskList), PuraNodeSupport.Rendered, "任务列表（UseGfm）"),
        new(typeof(FootnoteLink), PuraNodeSupport.Rendered, "脚注引用/回链（UseFootnotes）"),
        new(typeof(ContainerInline), PuraNodeSupport.Structural, "行内容器（强调/链接等）"),
        new(typeof(HtmlInline), PuraNodeSupport.Fallback, "HTML 已禁用；按文本回退"),
    ];

    /// <summary>是否属于 B 阶段公共契约（含结构/回退节点）。</summary>
    public static bool IsSupported(MarkdownObject node)
        => Describe(node) is not null;

    /// <summary>获取节点支持信息；未列入矩阵返回 null。</summary>
    public static PuraNodeSupportInfo? Describe(MarkdownObject node)
    {
        foreach (var info in Nodes)
        {
            if (info.NodeType.IsInstanceOfType(node))
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>矩阵中所有契约节点类型。</summary>
    public static IReadOnlyList<Type> SupportedNodeTypes
        => Nodes.Select(info => info.NodeType).ToArray();

    /// <summary>HTML 是否在语法层关闭（核心策略）。</summary>
    public const bool HtmlDisabled = true;
}



