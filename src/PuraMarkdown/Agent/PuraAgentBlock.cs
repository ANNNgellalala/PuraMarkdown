using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PuraMarkdown.Agent;

/// <summary>`pura-agent` 围栏对应的 AST 节点。</summary>
public sealed class PuraAgentBlock : LeafBlock
{
    /// <summary>PuraAgentBlock。</summary>
    /// <param name="parser">parser 参数。</param>
    public PuraAgentBlock(BlockParser parser) : base(parser)
    {
    }

    /// <summary>FencedChar。</summary>
    public char FencedChar { get; set; }

    /// <summary>OpeningFencedCharCount。</summary>
    public int OpeningFencedCharCount { get; set; }

    /// <summary>ClosingFencedCharCount。</summary>
    public int ClosingFencedCharCount { get; set; }

    /// <summary>是否已经出现闭合围栏；流式时用于判断占位状态。</summary>
    public bool IsClosed { get; set; }

    /// <summary>Info。</summary>
    public string? Info { get; set; }
}

/// <summary>
/// 拦截 info 为 <c>pura-agent</c> 的围栏代码块，产出 <see cref="PuraAgentBlock"/>。
/// 其它围栏返回 None，交回标准 FencedCodeBlockParser。
/// </summary>
public sealed class PuraAgentBlockParser : BlockParser
{
    /// <summary>FenceInfo。</summary>
    public const string FenceInfo = "pura-agent";

    /// <summary>PuraAgentBlockParser。</summary>
    public PuraAgentBlockParser()
    {
        OpeningCharacters = ['`', '~'];
    }

    /// <summary>TryOpen。</summary>
    /// <param name="processor">processor 参数。</param>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        var line = processor.Line;
        var fenceChar = line.CurrentChar;
        var fenceCount = 0;
        while (line.CurrentChar == fenceChar)
        {
            fenceCount++;
            line.SkipChar();
        }

        if (fenceCount < 3)
        {
            return BlockState.None;
        }

        line.TrimStart();
        var info = line.ToString().Trim();
        if (!string.Equals(info, FenceInfo, StringComparison.OrdinalIgnoreCase))
        {
            return BlockState.None;
        }

        var block = new PuraAgentBlock(this)
        {
            FencedChar = fenceChar,
            OpeningFencedCharCount = fenceCount,
            Info = info,
            Line = processor.LineIndex,
            Column = processor.Column,
            Span = new SourceSpan(processor.Start, processor.Line.End),
        };

        processor.NewBlocks.Push(block);
        return BlockState.ContinueDiscard;
    }

    /// <summary>TryContinue。</summary>
    /// <param name="processor">processor 参数。</param>
    /// <param name="block">block 参数。</param>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (block is not PuraAgentBlock agentBlock)
        {
            return BlockState.None;
        }

        if (processor.IsCodeIndent)
        {
            processor.GoToColumn(processor.ColumnBeforeIndent);
            return BlockState.Continue;
        }

        var line = processor.Line;
        var closingCount = 0;
        while (line.CurrentChar == agentBlock.FencedChar)
        {
            closingCount++;
            line.SkipChar();
        }

        if (closingCount >= agentBlock.OpeningFencedCharCount
            && (line.CurrentChar == '\0' || char.IsWhiteSpace(line.CurrentChar)))
        {
            agentBlock.ClosingFencedCharCount = closingCount;
            agentBlock.IsClosed = true;
            block.UpdateSpanEnd(processor.Start + closingCount - 1);
            return BlockState.BreakDiscard;
        }

        processor.GoToColumn(processor.ColumnBeforeIndent);
        return BlockState.Continue;
    }

    /// <summary>Close。</summary>
    /// <param name="processor">processor 参数。</param>
    /// <param name="block">block 参数。</param>
    public override bool Close(BlockProcessor processor, Block block)
    {
        if (block is PuraAgentBlock agentBlock)
        {
            agentBlock.IsClosed = agentBlock.ClosingFencedCharCount > 0;
        }

        return true;
    }
}

/// <summary>把 PuraAgentBlockParser 注册进 Markdig 管道。</summary>
public sealed class PuraAgentExtension : IMarkdownExtension
{
    /// <summary>Setup。</summary>
    /// <param name="pipeline">pipeline 参数。</param>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.BlockParsers.Contains<PuraAgentBlockParser>())
        {
            pipeline.BlockParsers.Insert(0, new PuraAgentBlockParser());
        }
    }

    /// <summary>Setup。</summary>
    /// <param name="pipeline">pipeline 参数。</param>
    /// <param name="renderer">renderer 参数。</param>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}
