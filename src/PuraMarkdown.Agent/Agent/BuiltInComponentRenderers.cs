using Avalonia.Controls;
using PuraMarkdown.Agent.Rendering;

namespace PuraMarkdown.Agent;

internal sealed class ChoiceComponentRenderer : IAgentComponentRenderer
{
    public string ComponentType => "choice";

    public Control RenderComponent(PuraAgentComponent component, RenderContext context)
        => new PuraChoiceControl((PuraChoiceComponent)component, context);
}

internal sealed class ConfirmComponentRenderer : IAgentComponentRenderer
{
    public string ComponentType => "confirm";

    public Control RenderComponent(PuraAgentComponent component, RenderContext context)
        => new PuraConfirmControl((PuraConfirmComponent)component, context);
}

internal sealed class ToolCallComponentRenderer : IAgentComponentRenderer
{
    public string ComponentType => "tool-call";

    public Control RenderComponent(PuraAgentComponent component, RenderContext context)
        => component is PuraToolCallComponent toolCall
            ? new PuraToolCallCard(toolCall.Call, context.Theme, context.Options.Localizer, context.Options.Clipboard)
            : throw new InvalidOperationException("tool-call 渲染器只处理 tool-call 组件");
}
