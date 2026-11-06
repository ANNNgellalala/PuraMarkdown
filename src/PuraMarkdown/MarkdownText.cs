using System.Text;
using Markdig.Syntax;

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

    public static string Quote(string? text) => text ?? string.Empty;
}
