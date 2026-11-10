using System.Text.Json;

namespace PuraMarkdown.Agent;

internal static class AgentJson
{
    /// <summary>TryGetType。</summary>
    /// <param name="root">root 参数。</param>
    /// <param name="type">type 参数。</param>
    public static bool TryGetType(JsonElement root, out string? type)
    {
        type = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
        {
            type = typeElement.GetString();
        }

        return type is not null;
    }

    /// <summary>TryReadString。</summary>
    /// <param name="obj">obj 参数。</param>
    /// <param name="name">name 参数。</param>
    /// <param name="required">required 参数。</param>
    /// <param name="value">value 参数。</param>
    /// <param name="error">error 参数。</param>
    public static bool TryReadString(JsonElement obj, string name, bool required, out string? value, out PuraComponentError? error)
    {
        value = null;
        error = null;
        if (!obj.TryGetProperty(name, out var element))
        {
            if (required)
            {
                error = new PuraComponentError($"缺少字段 {name}", name);
                return false;
            }
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = new PuraComponentError($"字段 {name} 必须是字符串", name);
            return false;
        }

        value = element.GetString();
        return true;
    }

    /// <summary>TryReadBool。</summary>
    /// <param name="obj">obj 参数。</param>
    /// <param name="name">name 参数。</param>
    /// <param name="defaultValue">defaultValue 参数。</param>
    /// <param name="value">value 参数。</param>
    /// <param name="error">error 参数。</param>
    public static bool TryReadBool(JsonElement obj, string name, bool defaultValue, out bool value, out PuraComponentError? error)
    {
        value = defaultValue;
        error = null;
        if (!obj.TryGetProperty(name, out var element))
        {
            return true;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = new PuraComponentError($"字段 {name} 必须是布尔值", name);
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    /// <summary>TryReadInt。</summary>
    /// <param name="obj">obj 参数。</param>
    /// <param name="name">name 参数。</param>
    /// <param name="defaultValue">defaultValue 参数。</param>
    /// <param name="value">value 参数。</param>
    /// <param name="error">error 参数。</param>
    public static bool TryReadInt(JsonElement obj, string name, int? defaultValue, out int? value, out PuraComponentError? error)
    {
        value = defaultValue;
        error = null;
        if (!obj.TryGetProperty(name, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number))
        {
            error = new PuraComponentError($"字段 {name} 必须是整数", name);
            return false;
        }

        value = number;
        return true;
    }
}
