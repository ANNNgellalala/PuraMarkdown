using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Syntax;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>confirm：确认/取消二选一，点击后上报并禁用。</summary>
internal sealed class PuraConfirmControl : Border
{
    private readonly RenderContext _context;
    private readonly string _componentId;
    private readonly bool _enabled;
    private bool _done;

    /// <summary>PuraConfirmControl。</summary>
    /// <param name="model">model 参数。</param>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public PuraConfirmControl(PuraConfirmComponent model, MarkdownObject node, RenderContext context)
    {
        _context = context;
        _componentId = model.Id ?? $"pura-agent-{node.Line}";
        _enabled = context.Options.InteractionEnabled && !context.IsStreaming;

        var colors = context.Theme.Colors;
        var typography = context.Theme.Typography;
        BorderBrush = new SolidColorBrush(colors.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(12);
        Background = Brushes.Transparent;
        AutomationProperties.SetName(this, model.Question);

        var root = new StackPanel { Spacing = 6 };
        Child = root;

        root.Children.Add(new TextBlock
        {
            Text = model.Question,
            FontSize = typography.BodySize + 1,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(colors.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            root.Children.Add(new TextBlock
            {
                Text = model.Description,
                FontSize = typography.BodySize - 1,
                Foreground = new SolidColorBrush(colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var confirm = new Button
        {
            Content = model.ConfirmLabel,
            IsEnabled = _enabled,
            Background = new SolidColorBrush(model.Danger ? colors.Danger : colors.Accent),
            Foreground = Brushes.White,
        };
        confirm.Click += (_, _) => Complete(true);

        var cancel = new Button
        {
            Content = model.CancelLabel,
            IsEnabled = _enabled,
        };
        cancel.Click += (_, _) => Complete(false);

        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        _confirmButton = confirm;
        _cancelButton = cancel;
    }

    private readonly Button _confirmButton;
    private readonly Button _cancelButton;

    /// <summary>ComponentId。</summary>
    public string ComponentId => _componentId;

    private void Complete(bool confirmed)
    {
        if (!_enabled || _done)
        {
            return;
        }

        _done = true;
        _confirmButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;
        _context.InteractionSink?.Invoke(new PuraInteractionEventArgs
        {
            ComponentType = "confirm",
            ComponentId = _componentId,
            Confirmed = confirmed,
        });
    }
}

