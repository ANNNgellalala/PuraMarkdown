using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PuraMarkdown.Agent.Chat;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Agent.Testing;
using Xunit;

namespace PuraMarkdown.Tests;

public sealed class ToolCallTests
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

    private const string SuccessPayload =
        """{"id":"call_1","name":"web_search","status":"success","arguments":{"q":"avalonia markdown"},"result":{"hits":12},"error":null,"durationMs":1200}""";

    [Fact]
    public void TryParse_maps_fields_and_raw_json_arguments()
    {
        Assert.True(PuraToolCall.TryParse(SuccessPayload, out var call, out var error), error);

        Assert.NotNull(call);
        Assert.Equal("call_1", call!.Id);
        Assert.Equal("web_search", call.Name);
        Assert.Equal(PuraToolCallStatus.Succeeded, call.Status);
        Assert.Equal("""{"q":"avalonia markdown"}""", call.Arguments);
        Assert.Equal("""{"hits":12}""", call.Result);
        Assert.Null(call.Error);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), call.Duration);
    }

    [Fact]
    public void TryParse_defaults_status_to_running()
    {
        Assert.True(
            PuraToolCall.TryParse("""{"name":"calculator"}""", out var call, out var error),
            error);

        Assert.NotNull(call);
        Assert.Equal(PuraToolCallStatus.Running, call!.Status);
        Assert.Empty(call.Id);
        Assert.Null(call.Arguments);
    }

    [Theory]
    [InlineData("success", PuraToolCallStatus.Succeeded)]
    [InlineData("succeeded", PuraToolCallStatus.Succeeded)]
    [InlineData("error", PuraToolCallStatus.Failed)]
    [InlineData("failed", PuraToolCallStatus.Failed)]
    [InlineData("cancelled", PuraToolCallStatus.Cancelled)]
    [InlineData("canceled", PuraToolCallStatus.Cancelled)]
    [InlineData("running", PuraToolCallStatus.Running)]
    public void TryParse_accepts_documented_status_values(string status, PuraToolCallStatus expected)
    {
        Assert.True(
            PuraToolCall.TryParse($$"""{"name":"tool","status":"{{status}}"}""", out var call, out var error),
            error);

        Assert.Equal(expected, call!.Status);
    }

    [Fact]
    public void TryParse_keeps_string_arguments_verbatim()
    {
        Assert.True(
            PuraToolCall.TryParse("""{"name":"echo","arguments":"你好，世界","result":"ok"}""", out var call, out var error),
            error);

        Assert.Equal("你好，世界", call!.Arguments);
        Assert.Equal("ok", call.Result);
    }

    [Theory]
    [InlineData("""{"id":"call_1","status":"success"}""")]
    [InlineData("""{"name":"web_search",}""")]
    [InlineData("not json")]
    [InlineData("""{"name":123}""")]
    [InlineData("""{"name":"tool","status":"unknown"}""")]
    [InlineData("""{"name":"tool","durationMs":-5}""")]
    [InlineData("[]")]
    public void TryParse_rejects_invalid_payloads_without_throwing(string json)
    {
        Assert.False(PuraToolCall.TryParse(json, out var call, out var error));
        Assert.Null(call);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Agent_plugin_exposes_tool_call_component()
    {
        var plugin = new AgentPlugin();

        Assert.Contains("tool-call", plugin.ComponentTypes);
        Assert.Contains("choice", plugin.ComponentTypes);
    }

    [AvaloniaFact]
    public void Tool_fence_renders_card_with_status_duration_and_sections()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = "```pura-tool\n" + SuccessPayload + "\n```";

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("web_search", dump);
        Assert.Contains("成功", dump);
        Assert.Contains("1.20 秒", dump);
        Assert.Contains("参数", dump);
        Assert.Contains("结果", dump);
        Assert.Contains("""{"hits":12}""", dump);
        Assert.Empty(warnings);

        var cards = ControlTreeDumper.Find<Border>(view)
            .Where(border => AutomationProperties.GetName(border) == "工具调用 web_search")
            .ToList();
        Assert.Single(cards);
    }

    [AvaloniaFact]
    public void Tool_fence_alias_renders_failed_card_with_error_text()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        view.Markdown = """
            ```pura-tool-call
            {"id":"call_9","name":"read_file","status":"cancelled","error":"用户取消","arguments":{"path":"a.md"}}
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("read_file", dump);
        Assert.Contains("已取消", dump);
        Assert.Contains("用户取消", dump);
        Assert.Contains("复制工具参数", string.Join('\n', ControlTreeDumper.Find<Button>(view).Select(AutomationProperties.GetName)));
    }

    [AvaloniaFact]
    public void Invalid_tool_fence_falls_back_to_code_block_and_warns()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = """
            ```pura-tool
            { "name": "web_search", "arguments": }
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("\"web_search\"", dump);
        Assert.DoesNotContain("运行中", dump);
        Assert.Contains(warnings, warning => warning.Code == "PURA-DATA-002");
    }

    [AvaloniaFact]
    public void Tool_fence_missing_name_falls_back_to_code_block_and_warns()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = "```pura-tool\n{\"id\":\"call_1\",\"status\":\"success\"}\n```";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("call_1", dump);
        Assert.Contains(warnings, warning => warning.Code.StartsWith("PURA-DATA", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Unclosed_tool_fence_renders_running_card()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = "```pura-tool\n{\"id\":\"call_1\",\"name\":\"web_search\",\"status\":\"success\"}";

        var dump = ControlTreeDumper.Dump(view);

        Assert.Contains("web_search", dump);
        Assert.Contains("运行中", dump);
        Assert.DoesNotContain("成功", dump);
        Assert.Empty(warnings);
    }

    [AvaloniaFact]
    public void Unclosed_tool_fence_while_streaming_renders_running_card()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var session = view.BeginStreaming(new PuraStreamingOptions { Synchronous = true });

        session.Append("```pura-tool\n{\"id\":\"call_1\",\"name\":\"web_search\"}");

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("web_search", dump);
        Assert.Contains("运行中", dump);
    }

    [AvaloniaFact]
    public void Pura_agent_tool_call_component_renders_card()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = """
            ```pura-agent
            {"type":"tool-call","version":"1.0","id":"t1","name":"read_file","status":"error","error":"permission denied","arguments":{"path":"a.md"}}
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("read_file", dump);
        Assert.Contains("失败", dump);
        Assert.Contains("permission denied", dump);
        Assert.Contains("""{"path":"a.md"}""", dump);
        Assert.Empty(warnings);
    }

    [AvaloniaFact]
    public void Pura_agent_tool_call_component_without_name_warns()
    {
        var view = new PuraMarkdownView { Options = CreateOptions() };
        var warnings = new List<PuraWarning>();
        view.Warning += (_, args) => warnings.Add(args.Warning);

        view.Markdown = """
            ```pura-agent
            {"type":"tool-call","version":"1.0","id":"t1"}
            ```
            """;

        var dump = ControlTreeDumper.Dump(view);
        Assert.Contains("组件校验失败", dump);
        Assert.Contains(warnings, warning => warning.Code == "PURA-DATA-002");
    }

    [AvaloniaFact]
    public void Chat_message_with_tool_calls_renders_cards()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        chat.AddMessage(new PuraChatMessage
        {
            Id = "m1",
            Role = PuraChatRole.Assistant,
            Markdown = "最终回答",
            ToolCalls =
            [
                new PuraToolCall
                {
                    Id = "call_1",
                    Name = "web_search",
                    Status = PuraToolCallStatus.Succeeded,
                    Arguments = """{"q":"avalonia"}""",
                    Result = """{"hits":12}""",
                },
            ],
        });

        var window = new Window { Width = 900, Height = 600, Content = chat };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dump = ControlTreeDumper.Dump(window);
        Assert.Contains("web_search", dump);
        Assert.Contains("成功", dump);
        Assert.Contains("最终回答", dump);
        window.Close();
    }

    [AvaloniaFact]
    public void Chat_tool_message_without_content_still_renders_cards()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        chat.AddMessage(new PuraChatMessage
        {
            Id = "m1",
            Role = PuraChatRole.Tool,
            ToolCallId = "call_1",
            ToolCalls =
            [
                new PuraToolCall
                {
                    Id = "call_1",
                    Name = "read_file",
                    Status = PuraToolCallStatus.Failed,
                    Error = "permission denied",
                },
            ],
        });

        var window = new Window { Width = 900, Height = 600, Content = chat };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dump = ControlTreeDumper.Dump(window);
        Assert.Contains("read_file", dump);
        Assert.Contains("permission denied", dump);
        Assert.Contains("失败", dump);
        window.Close();
    }

    [AvaloniaFact]
    public void Chat_snapshot_round_trips_tool_calls()
    {
        var chat = new PuraChatView { Options = CreateOptions() };
        chat.AddMessage(new PuraChatMessage
        {
            Id = "m1",
            Role = PuraChatRole.Tool,
            ToolCallId = "call_1",
            ToolCalls =
            [
                new PuraToolCall
                {
                    Id = "call_1",
                    Name = "web_search",
                    Status = PuraToolCallStatus.Failed,
                    Arguments = """{"q":"avalonia"}""",
                    Error = "timeout",
                    Duration = TimeSpan.FromMilliseconds(1200),
                },
            ],
        });

        var json = PuraChatSnapshotSerializer.Serialize(chat.CreateSnapshot());
        var restored = new PuraChatView { Options = CreateOptions() };
        restored.RestoreSnapshot(PuraChatSnapshotSerializer.Deserialize(json));

        var message = Assert.Single(restored.Messages);
        Assert.Equal(PuraChatRole.Tool, message.Role);
        Assert.Equal("call_1", message.ToolCallId);
        var call = Assert.Single(message.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("web_search", call.Name);
        Assert.Equal(PuraToolCallStatus.Failed, call.Status);
        Assert.Equal("timeout", call.Error);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), call.Duration);
    }
}
