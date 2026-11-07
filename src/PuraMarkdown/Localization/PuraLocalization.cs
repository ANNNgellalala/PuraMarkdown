namespace PuraMarkdown.Localization;

/// <summary>本地化字符串提供者。宿主可实现该接口替换内置文案（默认中文）。</summary>
public interface IPuraLocalizer
{
    /// <summary>按键获取文案；未知 key 返回 key 本身。</summary>
    /// <param name="key">文案键，见 <see cref="PuraStringKeys"/>。</param>
    /// <param name="args">格式参数。</param>
    string Get(string key, params object?[] args);
}

/// <summary>内置中文文案（默认实现，以中文为主）。</summary>
public sealed class ChinesePuraLocalizer : IPuraLocalizer
{
    /// <summary>默认实例。</summary>
    public static ChinesePuraLocalizer Instance { get; } = new();

    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        [PuraStringKeys.Copy] = "复制",
        [PuraStringKeys.Copied] = "已复制",
        [PuraStringKeys.CopyFailed] = "复制失败",
        [PuraStringKeys.CopyCodeAutomation] = "复制代码",
        [PuraStringKeys.CodeLanguageFallback] = "text",
        [PuraStringKeys.Image] = "图片",
        [PuraStringKeys.ImageBlocked] = "图片被拒绝：{0}",
        [PuraStringKeys.ImageNotLoaded] = "图片未加载：{0}",
        [PuraStringKeys.ImageLoadFailed] = "图片加载失败：{0}",
        [PuraStringKeys.ImageLoadException] = "图片加载异常：{0}",
        [PuraStringKeys.ComponentLoading] = "组件加载中…",
        [PuraStringKeys.ComponentPreparing] = "交互组件准备中…",
        [PuraStringKeys.UnsupportedComponent] = "不支持的组件类型",
        [PuraStringKeys.ComponentEmpty] = "组件载荷为空",
        [PuraStringKeys.ComponentEmptyDetail] = "pura-agent 围栏内没有 JSON 对象",
        [PuraStringKeys.ComponentTooLarge] = "组件载荷超限",
        [PuraStringKeys.ComponentTooLargeDetail] = "超过 {0} 字符",
        [PuraStringKeys.ComponentMustBeObject] = "组件载荷必须是 JSON 对象",
        [PuraStringKeys.ComponentMissingType] = "缺少 type 字段",
        [PuraStringKeys.ComponentJsonParseFailed] = "JSON 解析失败",
        [PuraStringKeys.ComponentValidationFailed] = "组件校验失败",
        [PuraStringKeys.ComponentSchemaFailed] = "组件 schema 校验失败",
        [PuraStringKeys.ComponentRenderFailed] = "组件渲染失败",
        [PuraStringKeys.ComponentUnknownValidation] = "未知校验错误",
        [PuraStringKeys.ChoiceSelectAtLeast] = "至少选择 {0} 项",
        [PuraStringKeys.ChoiceSelectAtMost] = "最多选择 {0} 项",
        [PuraStringKeys.StreamingCompleted] = "消息内容生成完成",
        [PuraStringKeys.StreamingAborted] = "消息生成已中断",
    };

    /// <inheritdoc/>
    public string Get(string key, params object?[] args)
    {
        if (!Strings.TryGetValue(key, out var template))
        {
            return key;
        }

        return args.Length == 0 ? template : string.Format(template, args);
    }
}

/// <summary>内置文案键。宿主实现 <see cref="IPuraLocalizer"/> 时可使用。</summary>
public static class PuraStringKeys
{
    /// <summary>文案键：Copy。</summary>
    public const string Copy = "copy";
    /// <summary>文案键：Copied。</summary>
    public const string Copied = "copied";
    /// <summary>文案键：CopyFailed。</summary>
    public const string CopyFailed = "copy-failed";
    /// <summary>文案键：CopyCodeAutomation。</summary>
    public const string CopyCodeAutomation = "copy-code-automation";
    /// <summary>文案键：CodeLanguageFallback。</summary>
    public const string CodeLanguageFallback = "code-language-fallback";
    /// <summary>文案键：Image。</summary>
    public const string Image = "image";
    /// <summary>文案键：ImageBlocked。</summary>
    /// <summary>文案键：ImagePlaceholder。</summary>
    public const string ImagePlaceholder = "image-placeholder";
    /// <summary>文案键：ImageBlocked。</summary>
    public const string ImageBlocked = "image-blocked";
    /// <summary>文案键：ImageNotLoaded。</summary>
    public const string ImageNotLoaded = "image-not-loaded";
    /// <summary>文案键：ImageLoadFailed。</summary>
    public const string ImageLoadFailed = "image-load-failed";
    /// <summary>文案键：ImageLoadException。</summary>
    public const string ImageLoadException = "image-load-exception";
    /// <summary>文案键：ComponentLoading。</summary>
    public const string ComponentLoading = "component-loading";
    /// <summary>文案键：ComponentPreparing。</summary>
    public const string ComponentPreparing = "component-preparing";
    /// <summary>文案键：UnsupportedComponent。</summary>
    public const string UnsupportedComponent = "unsupported-component";
    /// <summary>文案键：ComponentEmpty。</summary>
    public const string ComponentEmpty = "component-empty";
    /// <summary>文案键：ComponentEmptyDetail。</summary>
    public const string ComponentEmptyDetail = "component-empty-detail";
    /// <summary>文案键：ComponentTooLarge。</summary>
    public const string ComponentTooLarge = "component-too-large";
    /// <summary>文案键：ComponentTooLargeDetail。</summary>
    public const string ComponentTooLargeDetail = "component-too-large-detail";
    /// <summary>文案键：ComponentMustBeObject。</summary>
    public const string ComponentMustBeObject = "component-must-be-object";
    /// <summary>文案键：ComponentMissingType。</summary>
    public const string ComponentMissingType = "component-missing-type";
    /// <summary>文案键：ComponentJsonParseFailed。</summary>
    public const string ComponentJsonParseFailed = "component-json-parse-failed";
    /// <summary>文案键：ComponentValidationFailed。</summary>
    public const string ComponentValidationFailed = "component-validation-failed";
    /// <summary>文案键：ComponentSchemaFailed。</summary>
    public const string ComponentSchemaFailed = "component-schema-failed";
    /// <summary>文案键：ComponentRenderFailed。</summary>
    public const string ComponentRenderFailed = "component-render-failed";
    /// <summary>文案键：ComponentUnknownValidation。</summary>
    public const string ComponentUnknownValidation = "component-unknown-validation";
    /// <summary>文案键：ChoiceSelectAtLeast。</summary>
    public const string ChoiceSelectAtLeast = "choice-select-at-least";
    /// <summary>文案键：ChoiceSelectAtMost。</summary>
    public const string ChoiceSelectAtMost = "choice-select-at-most";
    /// <summary>文案键：StreamingCompleted。</summary>
    public const string StreamingCompleted = "streaming-completed";
    /// <summary>文案键：StreamingAborted。</summary>
    public const string StreamingAborted = "streaming-aborted";
}



