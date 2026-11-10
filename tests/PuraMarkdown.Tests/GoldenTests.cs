using PuraMarkdown.Agent.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class GoldenTests
{
    private static PuraMarkdownOptions CreateOptions()
        => new()
        {
            Pipeline = new PuraPipelineBuilder()
                .UseCommonMark()
                .UseGfm()
                .UseAgentComponents()
                .Build(),
            Theme = PuraTheme.Light,
        };

    [AvaloniaFact]
    public void Basic_document_matches_golden()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "# Hi\n\nHello **world**.";

        GoldenFile.AssertMatches("basic", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Table_matches_golden()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "| Name | Value |\n| --- | --- |\n| A | 1 |";

        GoldenFile.AssertMatches("table", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Choice_matches_golden()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = """
            ```pura-agent
            {
              "type": "choice",
              "version": "1.0",
              "id": "env",
              "question": "选择环境",
              "options": [
                { "id": "dev", "label": "开发" },
                { "id": "prod", "label": "生产", "description": "需要审批", "disabled": true }
              ],
              "defaultSelected": ["dev"]
            }
            ```
            """;

        GoldenFile.AssertMatches("choice", ControlTreeDumper.Dump(view));
    }

    [AvaloniaFact]
    public void Task_list_matches_golden()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = "- [x] done\n- [ ] todo";

        GoldenFile.AssertMatches("task-list", ControlTreeDumper.Dump(view));
    }
}



