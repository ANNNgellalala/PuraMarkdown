using Json.Schema;

namespace PuraMarkdown.Agent;

/// <summary>内置组件的 JSON Schema（与语法规范 §4/§5 对应）。</summary>
internal static class ComponentSchemas
{
    /// <summary>Choice。</summary>
    public static readonly JsonSchema Choice = JsonSchema.FromText(ChoiceSchema);

    /// <summary>Confirm。</summary>
    public static readonly JsonSchema Confirm = JsonSchema.FromText(ConfirmSchema);

    private const string ChoiceSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["type", "version", "question", "options"],
          "properties": {
            "type": { "const": "choice" },
            "version": { "type": "string", "minLength": 1, "maxLength": 16 },
            "id": { "type": "string", "minLength": 1, "maxLength": 64 },
            "question": { "type": "string", "minLength": 1, "maxLength": 500 },
            "description": { "type": "string", "maxLength": 1000 },
            "multi": { "type": "boolean" },
            "options": {
              "type": "array",
              "minItems": 1,
              "maxItems": 200,
              "items": {
                "type": "object",
                "required": ["id", "label"],
                "properties": {
                  "id": { "type": "string", "minLength": 1, "maxLength": 128 },
                  "label": { "type": "string", "minLength": 1, "maxLength": 500 },
                  "description": { "type": "string", "maxLength": 1000 },
                  "disabled": { "type": "boolean" }
                },
                "additionalProperties": true
              }
            },
            "defaultSelected": {
              "type": "array",
              "items": { "type": "string", "minLength": 1, "maxLength": 128 },
              "maxItems": 200
            },
            "min": { "type": "integer", "minimum": 0 },
            "max": { "type": "integer", "minimum": 0 },
            "submitLabel": { "type": "string", "maxLength": 64 },
            "disabled": { "type": "boolean" }
          },
          "additionalProperties": true
        }
        """;

    private const string ConfirmSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["type", "version", "question"],
          "properties": {
            "type": { "const": "confirm" },
            "version": { "type": "string", "minLength": 1, "maxLength": 16 },
            "id": { "type": "string", "minLength": 1, "maxLength": 64 },
            "question": { "type": "string", "minLength": 1, "maxLength": 500 },
            "description": { "type": "string", "maxLength": 2000 },
            "confirmLabel": { "type": "string", "minLength": 1, "maxLength": 64 },
            "cancelLabel": { "type": "string", "minLength": 1, "maxLength": 64 },
            "danger": { "type": "boolean" }
          },
          "additionalProperties": true
        }
        """;
}
