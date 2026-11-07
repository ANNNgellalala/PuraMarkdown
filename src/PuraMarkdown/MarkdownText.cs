using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PuraMarkdown;

internal static class MarkdownText
{
    /// <summary>取 LeafBlock 的原始文本（含换行）。</summary>
    public static string GetLeafText(LeafBlock block)
    {
        var builder = new StringBuilder();
        var lines = block.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            builder.Append(lines.Lines[i].Slice.ToString());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    public static string GetHeadingText(HeadingBlock heading)
        => GetInlineText(heading.Inline).Trim();

    public static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendInline(child, builder);
        }

        return builder.ToString();
    }

    public static string GetPlainText(MarkdownDocument document)
    {
        var builder = new StringBuilder();
        AppendBlocks(document, builder);
        return builder.ToString().TrimEnd();
    }

    private static void AppendBlocks(ContainerBlock container, StringBuilder builder)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case CodeBlock code:
                    builder.Append(GetLeafText(code).TrimEnd('\n'));
                    builder.Append('\n');
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    builder.Append(GetInlineText(leaf.Inline));
                    builder.Append('\n');
                    break;
                case ContainerBlock nested:
                    AppendBlocks(nested, builder);
                    break;
            }
        }
    }

    private static void AppendInline(Inline inline, StringBuilder builder)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline lineBreak:
                builder.Append(lineBreak.IsHard ? '\n' : ' ');
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case LinkInline link:
                builder.Append(GetInlineText(link));
                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                {
                    AppendInline(child, builder);
                }

                break;
        }
    }

    public static string Quote(string? text) => text ?? string.Empty;
}
