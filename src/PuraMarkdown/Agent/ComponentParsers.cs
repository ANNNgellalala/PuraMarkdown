using System.Text.Json;
using Json.Schema;

namespace PuraMarkdown.Agent;

/// <summary>ChoiceParser。</summary>
public sealed class ChoiceParser : IAgentComponentParser
{
    /// <summary>ComponentType。</summary>
    public string ComponentType => "choice";

    /// <summary>PayloadSchema。</summary>
    public JsonSchema PayloadSchema => ComponentSchemas.Choice;

    private static PuraComponentError WithPath(PuraComponentError? error, string path)
        => new(error?.Message ?? "字段无效", path);

    /// <summary>TryParse。</summary>
    /// <param name="json">json 参数。</param>
    /// <param name="component">component 参数。</param>
    /// <param name="error">error 参数。</param>
    public bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error)
    {
        component = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = new PuraComponentError($"JSON 语法错误：{ex.Message}");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = new PuraComponentError("载荷必须是 JSON 对象", "$");
                return false;
            }

            if (!AgentJson.TryReadString(root, "type", required: true, out var type, out error)
                || !string.Equals(type, "choice", StringComparison.OrdinalIgnoreCase))
            {
                error ??= new PuraComponentError("type 必须为 choice", "type");
                return false;
            }

            if (!AgentJson.TryReadString(root, "version", required: true, out var version, out error)
                || string.IsNullOrWhiteSpace(version))
            {
                error ??= new PuraComponentError("缺少 version", "version");
                return false;
            }

            if (!AgentJson.TryReadString(root, "id", required: false, out var id, out error)
                || !AgentJson.TryReadString(root, "question", required: true, out var question, out error)
                || string.IsNullOrWhiteSpace(question))
            {
                error ??= new PuraComponentError("question 不能为空", "question");
                return false;
            }

            if (question.Length > 500)
            {
                error = new PuraComponentError("question 超过 500 字符", "question");
                return false;
            }

            if (!AgentJson.TryReadString(root, "description", required: false, out var description, out error)
                || !AgentJson.TryReadBool(root, "multi", defaultValue: false, out var multi, out error)
                || !AgentJson.TryReadBool(root, "disabled", defaultValue: false, out var disabled, out error)
                || !AgentJson.TryReadString(root, "submitLabel", required: false, out var submitLabel, out error)
                || !AgentJson.TryReadInt(root, "min", defaultValue: null, out var min, out error))
            {
                return false;
            }

            int? max = null;
            if (!AgentJson.TryReadInt(root, "max", defaultValue: null, out max, out error))
            {
                return false;
            }

            if (!root.TryGetProperty("options", out var optionsElement) || optionsElement.ValueKind != JsonValueKind.Array)
            {
                error = new PuraComponentError("缺少 options 数组", "options");
                return false;
            }

            var options = new List<PuraChoiceOption>();
            var index = 0;
            foreach (var optionElement in optionsElement.EnumerateArray())
            {
                var path = $"options[{index}]";
                if (optionElement.ValueKind != JsonValueKind.Object)
                {
                    error = new PuraComponentError("选项必须是对象", path);
                    return false;
                }

                if (!AgentJson.TryReadString(optionElement, "id", required: true, out var optionId, out error)
                    || string.IsNullOrWhiteSpace(optionId))
                {
                    error = WithPath(error, $"{path}.id");
                    return false;
                }

                if (!AgentJson.TryReadString(optionElement, "label", required: true, out var label, out error)
                    || string.IsNullOrWhiteSpace(label))
                {
                    error = WithPath(error, $"{path}.label");
                    return false;
                }

                if (!AgentJson.TryReadString(optionElement, "description", required: false, out var optionDescription, out error))
                {
                    error = WithPath(error, $"{path}.description");
                    return false;
                }

                if (!AgentJson.TryReadBool(optionElement, "disabled", defaultValue: false, out var optionDisabled, out error))
                {
                    error = WithPath(error, $"{path}.disabled");
                    return false;
                }

                if (options.Any(o => string.Equals(o.Id, optionId, StringComparison.Ordinal)))
                {
                    error = new PuraComponentError($"选项 id 重复：{optionId}", $"{path}.id");
                    return false;
                }

                options.Add(new PuraChoiceOption(optionId, label, optionDescription, optionDisabled));
                index++;
            }

            if (options.Count == 0)
            {
                error = new PuraComponentError("options 至少需要 1 项", "options");
                return false;
            }

            if (options.Count > 200)
            {
                error = new PuraComponentError("options 超过 200 项", "options");
                return false;
            }

            var defaultSelected = new List<string>();
            if (root.TryGetProperty("defaultSelected", out var defaultElement))
            {
                if (defaultElement.ValueKind != JsonValueKind.Array)
                {
                    error = new PuraComponentError("defaultSelected 必须是数组", "defaultSelected");
                    return false;
                }

                foreach (var item in defaultElement.EnumerateArray())
                {
                    var selectedId = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (selectedId is null || options.All(o => o.Id != selectedId))
                    {
                        error = new PuraComponentError($"defaultSelected 引用了不存在的选项：{selectedId}", "defaultSelected");
                        return false;
                    }

                    defaultSelected.Add(selectedId);
                }

                if (!multi && defaultSelected.Count > 1)
                {
                    error = new PuraComponentError("单选组件的 defaultSelected 最多 1 项", "defaultSelected");
                    return false;
                }
            }

            if (min is < 0)
            {
                error = new PuraComponentError("min 不能为负数", "min");
                return false;
            }

            if (max is not null && max < (min ?? 0))
            {
                error = new PuraComponentError("max 必须大于等于 min", "max");
                return false;
            }

            component = new PuraChoiceComponent
            {
                Type = "choice",
                Version = version,
                Id = id,
                Question = question,
                Description = description,
                Multi = multi,
                Options = options,
                DefaultSelected = defaultSelected,
                Min = min ?? 0,
                Max = max,
                SubmitLabel = string.IsNullOrWhiteSpace(submitLabel) ? "确认" : submitLabel,
                Disabled = disabled,
            };
            return true;
        }
    }
}

/// <summary>ConfirmParser。</summary>
public sealed class ConfirmParser : IAgentComponentParser
{
    /// <summary>ComponentType。</summary>
    public string ComponentType => "confirm";

    /// <summary>PayloadSchema。</summary>
    public JsonSchema PayloadSchema => ComponentSchemas.Confirm;

    /// <summary>TryParse。</summary>
    /// <param name="json">json 参数。</param>
    /// <param name="component">component 参数。</param>
    /// <param name="error">error 参数。</param>
    public bool TryParse(string json, out PuraAgentComponent? component, out PuraComponentError? error)
    {
        component = null;
        error = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = new PuraComponentError($"JSON 语法错误：{ex.Message}");
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = new PuraComponentError("载荷必须是 JSON 对象", "$");
                return false;
            }

            if (!AgentJson.TryReadString(root, "type", required: true, out var type, out error)
                || !string.Equals(type, "confirm", StringComparison.OrdinalIgnoreCase))
            {
                error ??= new PuraComponentError("type 必须为 confirm", "type");
                return false;
            }

            if (!AgentJson.TryReadString(root, "version", required: true, out var version, out error)
                || string.IsNullOrWhiteSpace(version)
                || !AgentJson.TryReadString(root, "id", required: false, out var id, out error)
                || !AgentJson.TryReadString(root, "question", required: true, out var question, out error)
                || string.IsNullOrWhiteSpace(question))
            {
                error ??= new PuraComponentError("question 不能为空", "question");
                return false;
            }

            if (!AgentJson.TryReadString(root, "description", required: false, out var description, out error)
                || !AgentJson.TryReadString(root, "confirmLabel", required: false, out var confirmLabel, out error)
                || !AgentJson.TryReadString(root, "cancelLabel", required: false, out var cancelLabel, out error)
                || !AgentJson.TryReadBool(root, "danger", defaultValue: false, out var danger, out error))
            {
                return false;
            }

            component = new PuraConfirmComponent
            {
                Type = "confirm",
                Version = version,
                Id = id,
                Question = question,
                Description = description,
                ConfirmLabel = string.IsNullOrWhiteSpace(confirmLabel) ? "确认" : confirmLabel,
                CancelLabel = string.IsNullOrWhiteSpace(cancelLabel) ? "取消" : cancelLabel,
                Danger = danger,
            };
            return true;
        }
    }
}


