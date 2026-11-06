using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Syntax;
using PuraMarkdown.Rendering;

namespace PuraMarkdown.Agent;

/// <summary>choice：单选（点击即上报）或多选（确认按钮上报）。</summary>
internal sealed class PuraChoiceControl : Border
{
    private readonly PuraChoiceComponent _model;
    private readonly RenderContext _context;
    private readonly bool _enabled;
    private readonly string _componentId;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly List<RadioButton> _radioButtons = [];
    private readonly TextBlock _errorText;
    private Button? _submitButton;
    private bool _submitted;

    /// <summary>PuraChoiceControl。</summary>
    /// <param name="model">model 参数。</param>
    /// <param name="node">node 参数。</param>
    /// <param name="context">context 参数。</param>
    public PuraChoiceControl(PuraChoiceComponent model, MarkdownObject node, RenderContext context)
    {
        _model = model;
        _context = context;
        _componentId = model.Id ?? $"pura-agent-{node.Line}";
        _enabled = context.Options.InteractionEnabled && !context.IsStreaming && !model.Disabled;
        foreach (var defaultId in model.DefaultSelected)
        {
            _selected.Add(defaultId);
        }

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

        var optionsPanel = new StackPanel { Spacing = 4 };
        root.Children.Add(optionsPanel);

        foreach (var option in model.Options)
        {
            optionsPanel.Children.Add(model.Multi
                ? BuildCheckBox(option, colors, typography)
                : BuildRadioButton(option, colors, typography));
        }

        _errorText = new TextBlock
        {
            IsVisible = false,
            FontSize = typography.BodySize - 1,
            Foreground = new SolidColorBrush(colors.Danger),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(_errorText);

        if (model.Multi)
        {
            _submitButton = new Button
            {
                Content = model.SubmitLabel,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                IsEnabled = _enabled,
            };
            _submitButton.Click += (_, _) => Submit();
            root.Children.Add(_submitButton);
        }
    }

    /// <summary>ComponentId。</summary>
    public string ComponentId => _componentId;

    private CheckBox BuildCheckBox(PuraChoiceOption option, PuraColors colors, PuraTypography typography)
    {
        var checkBox = new CheckBox
        {
            Content = BuildOptionContent(option, colors, typography),
            IsChecked = _selected.Contains(option.Id),
            IsEnabled = _enabled && !option.Disabled,
        };
        AutomationProperties.SetName(checkBox, option.Label);
        checkBox.PropertyChanged += (_, e) =>        {
            if (e.Property.Name != "IsChecked")
            {
                return;
            }

            if (!_enabled || _submitted)
            {
                return;
            }

            if (checkBox.IsChecked == true)
            {
                _selected.Add(option.Id);
            }
            else
            {
                _selected.Remove(option.Id);
            }

            _errorText.IsVisible = false;
        };
        return checkBox;
    }

    private RadioButton BuildRadioButton(PuraChoiceOption option, PuraColors colors, PuraTypography typography)
    {
        var radio = new RadioButton
        {
            Content = BuildOptionContent(option, colors, typography),
            IsChecked = _selected.Contains(option.Id),
            IsEnabled = _enabled && !option.Disabled,
        };
        AutomationProperties.SetName(radio, option.Label);
        radio.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != "IsChecked" || radio.IsChecked != true || !_enabled || _submitted)
            {
                return;
            }

            foreach (var other in _radioButtons)
            {
                if (!ReferenceEquals(other, radio))
                {
                    other.IsChecked = false;
                }
            }

            _selected.Clear();
            _selected.Add(option.Id);
            RaiseInteraction();
        };
        _radioButtons.Add(radio);
        return radio;
    }

    private static Control BuildOptionContent(PuraChoiceOption option, PuraColors colors, PuraTypography typography)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock
        {
            Text = option.Label,
            FontSize = typography.BodySize,
            Foreground = new SolidColorBrush(colors.Text),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(option.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = option.Description,
                FontSize = typography.BodySize - 1,
                Foreground = new SolidColorBrush(colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return panel;
    }

    private void Submit()
    {
        if (!_enabled || _submitted)
        {
            return;
        }

        var selected = _model.Options.Where(o => _selected.Contains(o.Id)).Select(o => o.Id).ToArray();
        if (selected.Length < Math.Max(1, _model.Min))
        {
            ShowError($"至少选择 {Math.Max(1, _model.Min)} 项");
            return;
        }

        if (_model.Max is { } max && selected.Length > max)
        {
            ShowError($"最多选择 {max} 项");
            return;
        }

        RaiseInteraction();
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    private void RaiseInteraction()
    {
        _submitted = true;
        if (_submitButton is not null)
        {
            _submitButton.IsEnabled = false;
        }

        var selected = _model.Options.Where(o => _selected.Contains(o.Id)).Select(o => o.Id).ToArray();
        _context.InteractionSink?.Invoke(new PuraInteractionEventArgs
        {
            ComponentType = "choice",
            ComponentId = _componentId,
            SelectedIds = selected,
        });
    }
}

