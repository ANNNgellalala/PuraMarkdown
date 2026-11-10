using Avalonia.Controls;
using Avalonia.Media;

namespace PuraMarkdown.Agent.Rendering;

/// <summary>按选项创建文本控件：开启选择时使用 SelectableTextBlock。</summary>
internal static class PuraTextBlockFactory
{
    public static TextBlock Create(RenderContext context, bool wrap = false)
    {
        var block = context.Options.EnableTextSelection
            ? new SelectableTextBlock()
            : new TextBlock();
        if (wrap)
        {
            block.TextWrapping = TextWrapping.Wrap;
        }

        return block;
    }
}
