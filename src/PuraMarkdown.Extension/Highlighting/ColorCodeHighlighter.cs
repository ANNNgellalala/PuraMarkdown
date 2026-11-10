using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;
using PuraMarkdown.Agent.Rendering;
using PuraMarkdown.Agent;

namespace PuraMarkdown.Extension.Highlighting.ColorCode;

/// <summary>基于 ColorCode.Core 的 <see cref="ICodeHighlighter"/> 实现。</summary>
public sealed class ColorCodeHighlighter : ICodeHighlighter
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp",
        ["c#"] = "csharp",
        ["js"] = "javascript",
        ["ts"] = "typescript",
        ["py"] = "python",
        ["sh"] = "powershell",
        ["shell"] = "powershell",
        ["html"] = "html",
        ["yml"] = "yaml",
        ["md"] = "markdown",
    };

    /// <summary>CanHighlight。</summary>
    /// <param name="language">language 参数。</param>
    public bool CanHighlight(string? language)
        => language is not null && ResolveLanguage(language) is not null;

    /// <summary>Highlight。</summary>
    /// <param name="code">code 参数。</param>
    /// <param name="language">language 参数。</param>
    public IReadOnlyList<HighlightSpan> Highlight(string code, string? language)
    {
        var resolved = language is null ? null : ResolveLanguage(language);
        if (resolved is null)
        {
            return [];
        }

        try
        {
            return new ScopeCollector().Collect(code, resolved);
        }
        catch
        {
            return [];
        }
    }

    internal static ILanguage? ResolveLanguage(string language)
    {
        var id = language.Trim();
        if (Aliases.TryGetValue(id, out var alias))
        {
            id = alias;
        }

        try
        {
            return Languages.FindById(id.ToLowerInvariant());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把 ColorCode 的 scope 树收集为 token span（只取叶子，避免重叠）。</summary>
    private sealed class ScopeCollector : CodeColorizerBase
    {
        private readonly List<HighlightSpan> _spans = [];
        private int _offset;

        /// <summary>ScopeCollector。</summary>
        public ScopeCollector() : base(StyleDictionary.DefaultLight, null!)
        {
        }

        /// <summary>Collect。</summary>
        /// <param name="code">code 参数。</param>
        /// <param name="language">language 参数。</param>
        public IReadOnlyList<HighlightSpan> Collect(string code, ILanguage language)
        {
            _spans.Clear();
            _offset = 0;
            languageParser.Parse(code, language, (fragment, scopes) =>
            {
                foreach (var scope in scopes)
                {
                    AddScope(scope);
                }

                _offset += fragment.Length;
            });

            return _spans.OrderBy(span => span.Start).ToArray();
        }

        protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        {
            // 收集在 Collect 的 parse 回调中完成；CodeColorizerBase 的抽象方法无额外用途。
        }

        private void AddScope(Scope scope)
        {
            if (scope.Children.Count == 0)
            {
                _spans.Add(new HighlightSpan(_offset + scope.Index, scope.Length, scope.Name));
                return;
            }

            foreach (var child in scope.Children)
            {
                AddScope(child);
            }
        }
    }
}

/// <summary>把 ColorCode 高亮接入 PuraMarkdown 的插件。</summary>
public sealed class ColorCodePlugin : IPuraPlugin
{
    /// <summary>Name。</summary>
    public string Name => "pura.highlighting.colorcode";

    /// <summary>Version。</summary>
    public string Version => "1.0.0";

    /// <summary>Description。</summary>
    public string? Description => "ColorCode.Core 语法高亮";

    /// <summary>Configure。</summary>
    /// <param name="builder">builder 参数。</param>
    public void Configure(PuraPipelineBuilder builder)
        => builder.UseCodeHighlighter(new ColorCodeHighlighter());
}

