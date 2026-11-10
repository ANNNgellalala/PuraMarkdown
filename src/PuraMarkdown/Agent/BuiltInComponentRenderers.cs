using Avalonia.Controls;
using PuraMarkdown.Rendering;

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
