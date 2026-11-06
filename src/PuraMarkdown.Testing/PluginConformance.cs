namespace PuraMarkdown.Testing;

/// <summary>插件一致性检查：名字/版本、注册期校验、构建成功。</summary>
public static class PluginConformance
{
    /// <summary>VerifyRegistration。</summary>
    /// <param name="plugin">plugin 参数。</param>
    public static PuraPipeline VerifyRegistration(IPuraPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var builder = new PuraPipelineBuilder()
            .UseCommonMark()
            .UseGfm()
            .UseAgentComponents()
            .UsePlugin(plugin);

        var pipeline = builder.Build();
        if (!pipeline.Environment.PluginNames.Contains(plugin.Name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"插件 {plugin.Name} 未登记到管道环境");
        }

        return pipeline;
    }

    /// <summary>RenderSample。</summary>
    /// <param name="markdown">markdown 参数。</param>
    /// <param name="pipeline">pipeline 参数。</param>
    public static string RenderSample(string markdown, PuraPipeline pipeline)
    {
        var view = new PuraMarkdownView
        {
            Options = new PuraMarkdownOptions { Pipeline = pipeline },
        };
        view.Markdown = markdown;
        return ControlTreeDumper.Dump(view);
    }
}
