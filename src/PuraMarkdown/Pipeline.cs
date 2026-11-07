using Markdig;
using Markdig.Syntax;
using PuraMarkdown.Rendering;
using PuraMarkdown.Security;
using PuraMarkdown.Syntax;

namespace PuraMarkdown;

/// <summary>插件入口：在 <see cref="Configure"/> 中注册围栏/行内/块渲染器、高亮与清洗器。</summary>
public interface IPuraPlugin
{
    /// <summary>插件唯一名称（构建期去重）。</summary>
    string Name { get; }

    /// <summary>插件版本（语义化版本）。</summary>
    string Version { get; }

    /// <summary>插件说明。</summary>
    string? Description { get; }

    /// <summary>注册插件能力。</summary>
    /// <param name="builder">管道构建器。</param>
    void Configure(PuraPipelineBuilder builder);
}

/// <summary>管道构建期配置错误（fail fast）。</summary>
public sealed class PuraConfigurationException(string message) : Exception(message);

/// <summary>冻结后的渲染环境（进程级复用）。</summary>
public sealed class PuraRenderEnvironment
{
    /// <summary>块/行内渲染器注册表。</summary>
    public required PuraRendererRegistry Renderers { get; init; }

    /// <summary>AST 清洗策略。</summary>
    public required IAstSanitizer Sanitizer { get; init; }

    /// <summary>代码高亮策略。</summary>
    public required ICodeHighlighter Highlighter { get; init; }

    /// <summary>已注册插件名称。</summary>
    public IReadOnlyList<string> PluginNames { get; init; } = [];
}

/// <summary>冻结后的解析 + 渲染环境。</summary>
public sealed class PuraPipeline
{
    internal PuraPipeline(PuraSyntaxPipeline syntax, PuraRenderEnvironment environment)
    {
        Syntax = syntax;
        Environment = environment;
    }

    /// <summary>语法层（Markdig 解析、PuraDocument、扫描器）。</summary>
    public PuraSyntaxPipeline Syntax { get; }

    /// <summary>渲染环境。</summary>
    public PuraRenderEnvironment Environment { get; }

    /// <summary>解析 Markdown 文本。</summary>
    /// <param name="markdown">Markdown 源文本。</param>
    public MarkdownDocument Parse(string markdown) => Syntax.Parse(markdown);

    /// <summary>解析为带诊断/大纲的文档。</summary>
    /// <param name="markdown">Markdown 源文本。</param>
    /// <param name="version">文档版本。</param>
    public PuraDocument ParseDocument(string markdown, int version = 0)
        => Syntax.ParseDocument(markdown, version);
}

/// <summary>构建 Markdig 管道与渲染器注册表；只在启动时使用。</summary>
public sealed class PuraPipelineBuilder
{
    private readonly PuraSyntaxBuilder _syntax = new();
    private readonly List<RendererRegistration> _renderers = [];
    private readonly List<FenceRendererRegistration> _fenceRenderers = [];
    private readonly List<IPuraPlugin> _plugins = [];
    private readonly HashSet<string> _pluginNames = new(StringComparer.OrdinalIgnoreCase);
    private ICodeHighlighter _highlighter = NullCodeHighlighter.Instance;
    private IAstSanitizer _sanitizer = new DefaultAstSanitizer();

    /// <summary>启用 CommonMark 基础能力（含精确源码位置）。</summary>
    public PuraPipelineBuilder UseCommonMark()
    {
        _syntax.UseCommonMark();
        return this;
    }

    /// <summary>启用 GFM：表格、任务列表、强调扩展、自动链接。</summary>
    public PuraPipelineBuilder UseGfm()
    {
        _syntax.UseGfm();
        return this;
    }

    /// <summary>注册块级渲染器（也可注册行内渲染器，但推荐用 <see cref="UseInlineRenderer"/>）。</summary>
    /// <param name="renderer">渲染器。</param>
    /// <param name="priority">优先级，越大越先尝试。</param>
    public PuraPipelineBuilder UseRenderer(IPuraNodeRenderer renderer, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers.Add(new RendererRegistration(renderer, priority));
        return this;
    }

    /// <summary>注册行内渲染器（未知 inline 节点会按优先级询问）。</summary>
    /// <param name="renderer">行内渲染器。</param>
    /// <param name="priority">优先级，越大越先尝试。</param>
    public PuraPipelineBuilder UseInlineRenderer(IPuraInlineRenderer renderer, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers.Add(new RendererRegistration(renderer, priority));
        return this;
    }

    /// <summary>注册围栏渲染器（```lang 代码围栏）。</summary>
    /// <param name="renderer">围栏渲染器。</param>
    /// <param name="priority">优先级，越大越先尝试。</param>
    public PuraPipelineBuilder UseFenceRenderer(IFenceRenderer renderer, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _fenceRenderers.Add(new FenceRendererRegistration(renderer, priority));
        return this;
    }

    /// <summary>设置代码高亮策略。</summary>
    /// <param name="highlighter">高亮器。</param>
    public PuraPipelineBuilder UseCodeHighlighter(ICodeHighlighter highlighter)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        _highlighter = highlighter;
        return this;
    }

    /// <summary>设置 AST 清洗策略（URL 策略、预算、节点阻断）。</summary>
    /// <param name="sanitizer">清洗器。</param>
    public PuraPipelineBuilder UseSanitizer(IAstSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        _sanitizer = sanitizer;
        return this;
    }

    /// <summary>注册插件。</summary>
    /// <param name="plugin">插件。</param>
    public PuraPipelineBuilder UsePlugin(IPuraPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (string.IsNullOrWhiteSpace(plugin.Name))
        {
            throw new PuraConfigurationException($"插件 {plugin.GetType().Name} 的 Name 不能为空");
        }

        if (string.IsNullOrWhiteSpace(plugin.Version))
        {
            throw new PuraConfigurationException($"插件 {plugin.Name} 的 Version 不能为空");
        }

        if (!_pluginNames.Add(plugin.Name))
        {
            throw new PuraConfigurationException($"插件重复注册：{plugin.Name}");
        }

        _plugins.Add(plugin);
        plugin.Configure(this);
        return this;
    }

    /// <summary>注册 Markdig 扩展（行内/块级自定义语法的高级入口）。</summary>
    /// <param name="extension">Markdig 扩展。</param>
    public PuraPipelineBuilder UseMarkdownExtension(IMarkdownExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _syntax.UseMarkdownExtension(extension);
        return this;
    }

    /// <summary>构建不可变管道；重复/非法注册在此 fail fast。</summary>
    public PuraPipeline Build()
    {
        ValidateRegistrations();

        var renderers = new List<RendererRegistration>(_renderers)
        {
            new(new ParagraphRenderer(), 0),
            new(new HeadingRenderer(), 0),
            new(new ListRenderer(), 0),
            new(new QuoteRenderer(), 0),
            new(new CodeBlockRenderer(), 0),
            new(new TableRenderer(), 0),
            new(new ThematicBreakRenderer(), 0),
            new(new HtmlFallbackRenderer(), 0),
            new(new FallbackTextRenderer(), int.MinValue),
        };

        if (_fenceRenderers.Count > 0)
        {
            renderers.Add(new RendererRegistration(
                new FenceDispatchRenderer(_fenceRenderers
                    .OrderByDescending(registration => registration.Priority)
                    .Select(registration => registration.Renderer)
                    .ToArray()),
                50));
        }

        var environment = new PuraRenderEnvironment
        {
            Renderers = new PuraRendererRegistry(renderers),
            Sanitizer = _sanitizer,
            Highlighter = _highlighter,
            PluginNames = _plugins.Select(plugin => plugin.Name).ToArray(),
        };

        return new PuraPipeline(_syntax.Build(), environment);
    }

    private void ValidateRegistrations()
    {
        if (_renderers.Any(registration => registration.Renderer is null))
        {
            throw new PuraConfigurationException("不允许注册 null 渲染器");
        }

        var duplicateFenceName = _fenceRenderers
            .GroupBy(registration => registration.Renderer.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFenceName is not null)
        {
            throw new PuraConfigurationException($"围栏渲染器重复注册：{duplicateFenceName.Key}");
        }
    }
}



