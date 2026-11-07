using PuraMarkdown.Testing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using PuraMarkdown.Agent;
using Avalonia.Interactivity;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class AgentChoiceTests
{
    private static PuraMarkdownOptions CreateOptions(bool interactionEnabled = true)
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .Build(),
            InteractionEnabled = interactionEnabled,
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public void Single_choice_reports_immediately_on_selection()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            {
              "type": "choice",
              "version": "1.0",
              "id": "env",
              "question": "选择环境",
              "options": [
                { "id": "dev", "label": "开发" },
                { "id": "prod", "label": "生产" }
              ]
            }
            ```
            """;

        var radios = ControlTreeDumper.Find<RadioButton>(view).ToList();
        Assert.Equal(2, radios.Count);

        radios[1].IsChecked = true;

        Assert.NotNull(captured);
        Assert.Equal("choice", captured!.ComponentType);
        Assert.Equal("env", captured.ComponentId);
        Assert.Equal(["prod"], captured.SelectedIds);
    }

    [Fact]
    public void Interaction_states_serialize_round_trip()
    {
        var json = PuraInteractionStates.Serialize([
            new PuraInteractionEventArgs
            {
                ComponentType = "choice",
                ComponentId = "env",
                SelectedIds = ["prod"],
                Timestamp = DateTimeOffset.Parse("2026-11-07T12:00:00+08:00"),
            },
        ]);

        var restored = PuraInteractionStates.Deserialize(json);

        var state = Assert.Single(restored);
        Assert.Equal("prod", state.SelectedIds.Single());
        Assert.Equal("env", state.ComponentId);
    }

    [AvaloniaFact]
    public void Interaction_state_can_be_restored_before_render()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.ApplyInteractionStates([
            new PuraInteractionEventArgs
            {
                ComponentType = "choice",
                ComponentId = "env",
                SelectedIds = ["prod"],
            },
        ]);

        view.Markdown = """
            ```pura-agent
            {
              "type": "choice",
              "version": "1.0",
              "id": "env",
              "question": "选择环境",
              "options": [
                { "id": "dev", "label": "开发" },
                { "id": "prod", "label": "生产" }
              ]
            }
            ```
            """;

        var radios = ControlTreeDumper.Find<RadioButton>(view).ToList();
        Assert.True(radios[1].IsChecked);
        Assert.Equal("prod", view.InteractionStates["env"].SelectedIds.Single());
    }

    [AvaloniaFact]
    public void Multi_choice_reports_only_after_confirm()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            {
              "type": "choice",
              "version": "1.0",
              "id": "tags",
              "question": "选择标签",
              "multi": true,
              "min": 1,
              "max": 2,
              "options": [
                { "id": "a", "label": "A" },
                { "id": "b", "label": "B" },
                { "id": "c", "label": "C" }
              ]
            }
            ```
            """;

        var checkBoxes = ControlTreeDumper.Find<CheckBox>(view).ToList();
        Assert.Equal(3, checkBoxes.Count);

        checkBoxes[0].IsChecked = true;
        checkBoxes[1].IsChecked = true;
        Assert.Null(captured);

        var confirm = ControlTreeDumper.Find<Button>(view).Single(b => b.Content is string text && text == "确认");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(captured);
        Assert.Equal("tags", captured!.ComponentId);
        Assert.Equal(["a", "b"], captured.SelectedIds);
    }

    [AvaloniaFact]
    public void Multi_choice_enforces_max()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            {
              "type": "choice",
              "version": "1.0",
              "id": "tags",
              "question": "选择标签",
              "multi": true,
              "max": 1,
              "options": [
                { "id": "a", "label": "A" },
                { "id": "b", "label": "B" }
              ]
            }
            ```
            """;

        var checkBoxes = ControlTreeDumper.Find<CheckBox>(view).ToList();
        checkBoxes[0].IsChecked = true;
        checkBoxes[1].IsChecked = true;

        var confirm = ControlTreeDumper.Find<Button>(view).Single(b => b.Content is string text && text == "确认");
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(captured);
        Assert.Contains("最多选择 1 项", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Confirm_reports_boolean()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        PuraInteractionEventArgs? captured = null;
        view.InteractionOccurred += (_, args) => captured = args;

        view.Markdown = """
            ```pura-agent
            { "type": "confirm", "version": "1.0", "id": "del", "question": "确定删除？" }
            ```
            """;

        var buttons = ControlTreeDumper.Find<Button>(view).ToList();
        var confirmButton = buttons.Single(b => b.Content is string text && text == "确认");
        confirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotNull(captured);
        Assert.Equal("confirm", captured!.ComponentType);
        Assert.True(captured.Confirmed);
    }
}





