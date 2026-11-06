using Markdig;
using Markdig.Syntax;
using PuraMarkdown.Agent;
using PuraMarkdown.Rendering;
using PuraMarkdown.Security;

namespace PuraMarkdown;

/// <summary>IPuraPlugin。</summary>
public interface IPuraPlugin
{
    /// <summary>Name。</summary>
    string Name { get; }

    /// <summary>Version。</summary>
    string Version { get; }

    /// <summary>Description。</summary>
    string? Description { get; }

    /// <summary>Configure。</summary>
    /// <param name="builder">builder 参数。</param>
    void Configure(PuraPipelineBuilder builder);
}

/// <summary>管道构建期配置错误（fail fast）。</summary>
public sealed class PuraConfigurationException(string message) : Exception(message);

/// <summary>PuraRenderEnvironment。</summary>
public sealed class PuraRenderEnvironment
{
    /// <summary>Renderers。</summary>
    public required PuraRendererRegistry Renderers { get; init; }
    /// <summary>Components。</summary>
    public required PuraComponentRegistry Components { get; init; }
    /// <summary>Sanitizer。</summary>
    public required IAstSanitizer Sanitizer { get; init; }
    /// <summary>Highlighter。</summary>
    public required ICodeHighlighter Highlighter { get; init; }
    /// <summary>Diagrams。</summary>
    public required IReadOnlyList<IDiagramRenderer> Diagrams { get; init; }
    /// <summary>Limits。</summary>
    public required PuraLimits Limits { get; init; }
    /// <summary>PluginNames。</summary>
    public IReadOnlyList<string> PluginNames { get; init; } = [];
}

/// <summary>冻结后的解析 + 渲染环境，进程级复用。</summary>
public sealed class PuraPipeline
{
    internal PuraPipeline(MarkdownPipeline markdownPipeline, PuraRenderEnvironment environment)
    {
        MarkdownPipeline = markdownPipeline;
        Environment = environment;
    }

    internal MarkdownPipeline MarkdownPipeline { get; }

    /// <summary>Environment。</summary>
    public PuraRenderEnvironment Environment { get; }

    /// <summary>Parse。</summary>
    /// <param name="markdown">markdown 参数。</param>
    public MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown, MarkdownPipeline);
}

/// <summary>构建 Markdig 管道与渲染器/组件注册表；只在启动时使用。</summary>
public sealed class PuraPipelineBuilder
{
    private readonly MarkdownPipelineBuilder _markdown = new();
    private readonly List<RendererRegistration> _renderers = [];
    private readonly List<IAgentComponentParser> _parsers = [];
    private readonly List<IAgentComponentRenderer> _componentRenderers = [];
    private readonly List<IDiagramRenderer> _diagrams = [];
    private readonly List<IPuraPlugin> _plugins = [];
    private readonly HashSet<string> _pluginNames = new(StringComparer.OrdinalIgnoreCase);
    private ICodeHighlighter _highlighter = NullCodeHighlighter.Instance;
    private IAstSanitizer _sanitizer = new DefaultAstSanitizer();
    private PuraLimits _limits = new();
    private bool _agentExtensionAdded;

    /// <summary>UseCommonMark。</summary>
    public PuraPipelineBuilder UseCommonMark()
    {
        _markdown.UsePreciseSourceLocation();
        return this;
    }

    /// <summary>UseGfm。</summary>
    public PuraPipelineBuilder UseGfm()
    {
        _markdown
            .UsePipeTables()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseAutoLinks();
        return this;
    }

    /// <summary>UseAgentComponents。</summary>
    public PuraPipelineBuilder UseAgentComponents()
    {
        if (_parsers.Any(p => p.ComponentType == "choice"))
        {
            return this;
        }

        _parsers.Add(new ChoiceParser());
        _parsers.Add(new ConfirmParser());
        _componentRenderers.Add(new ChoiceComponentRenderer());
        _componentRenderers.Add(new ConfirmComponentRenderer());
        EnsureAgentExtension();
        return this;
    }

    private void EnsureAgentExtension()
    {
        if (_agentExtensionAdded)
        {
            return;
        }

        _markdown.Extensions.Add(new PuraAgentExtension());
        _agentExtensionAdded = true;
    }

    /// <summary>UseRenderer。</summary>
    /// <param name="renderer">renderer 参数。</param>
    /// <param name="priority">priority 参数。</param>
    public PuraPipelineBuilder UseRenderer(IPuraRenderer renderer, int priority = 100)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers.Add(new RendererRegistration(renderer, priority));
        return this;
    }

    /// <summary>UseAgentComponentParser。</summary>
    /// <param name="parser">parser 参数。</param>
    public PuraPipelineBuilder UseAgentComponentParser(IAgentComponentParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parsers.Add(parser);
        return this;
    }

    /// <summary>UseAgentComponentRenderer。</summary>
    /// <param name="renderer">renderer 参数。</param>
    public PuraPipelineBuilder UseAgentComponentRenderer(IAgentComponentRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _componentRenderers.Add(renderer);
        return this;
    }

    /// <summary>UseDiagramRenderer。</summary>
    /// <param name="renderer">renderer 参数。</param>
    public PuraPipelineBuilder UseDiagramRenderer(IDiagramRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _diagrams.Add(renderer);
        return this;
    }

    /// <summary>UseCodeHighlighter。</summary>
    /// <param name="highlighter">highlighter 参数。</param>
    public PuraPipelineBuilder UseCodeHighlighter(ICodeHighlighter highlighter)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        _highlighter = highlighter;
        return this;
    }

    /// <summary>UseLimits。</summary>
    /// <param name="configure">configure 参数。</param>
    public PuraPipelineBuilder UseLimits(Action<PuraLimits> configure)
    {
        configure(_limits);
        return this;
    }

    /// <summary>UsePlugin。</summary>
    /// <param name="plugin">plugin 参数。</param>
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

    /// <summary>UseMarkdownExtension。</summary>
    /// <param name="extension">extension 参数。</param>
    public PuraPipelineBuilder UseMarkdownExtension(IMarkdownExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _markdown.Extensions.Add(extension);
        return this;
    }

    /// <summary>Build。</summary>
    public PuraPipeline Build()
    {
        _markdown
            .UsePreciseSourceLocation()
            .DisableHtml();

        ValidateRegistrations();

        if (_parsers.Count > 0 || _componentRenderers.Count > 0)
        {
            EnsureAgentExtension();
        }

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

        if (_diagrams.Count > 0)
        {
            renderers.Add(new RendererRegistration(new DiagramDispatchRenderer(), 50));
        }

        if (_parsers.Count > 0 || _componentRenderers.Count > 0)
        {
            renderers.Add(new RendererRegistration(new AgentComponentRenderer(), AgentComponentRenderer.DefaultPriority));
        }

        var environment = new PuraRenderEnvironment
        {
            Renderers = new PuraRendererRegistry(renderers),
            Components = new PuraComponentRegistry(_parsers, _componentRenderers),
            Sanitizer = _sanitizer,
            Highlighter = _highlighter,
            Diagrams = _diagrams,
            Limits = _limits,
            PluginNames = _plugins.Select(plugin => plugin.Name).ToArray(),
        };

        return new PuraPipeline(_markdown.Build(), environment);
    }

    private void ValidateRegistrations()
    {
        var duplicateParser = _parsers
            .GroupBy(parser => parser.ComponentType, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateParser is not null)
        {
            throw new PuraConfigurationException($"组件解析器重复注册：{duplicateParser.Key}");
        }

        var duplicateRenderer = _componentRenderers
            .GroupBy(renderer => renderer.ComponentType, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRenderer is not null)
        {
            throw new PuraConfigurationException($"组件渲染器重复注册：{duplicateRenderer.Key}");
        }

        if (_renderers.Any(registration => registration.Renderer is null))
        {
            throw new PuraConfigurationException("不允许注册 null 渲染器");
        }
    }
}
