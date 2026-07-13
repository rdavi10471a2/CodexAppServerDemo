using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexAppServerBlazor.Services;

public enum ElicitationFieldKind
{
    Boolean,
    String,
    Number,
    Enum
}

public sealed record ElicitationFieldOption(
    string Value,
    string Label);

public sealed record ElicitationFieldDefinition(
    string Name,
    ElicitationFieldKind Kind,
    string Label,
    string? Description,
    bool IsRequired,
    bool? DefaultBoolean,
    string? DefaultText,
    decimal? DefaultNumber,
    IReadOnlyList<ElicitationFieldOption> Options);

public sealed record ElicitationRequestSchema(
    string? Message,
    string Mode,
    IReadOnlyList<ElicitationFieldDefinition> Fields);

public sealed record PermissionApprovalRequest(
    int RequestId,
    JsonObject? ElicitationContent);

public static class ElicitationRequestSchemaParser
{
    public static ElicitationRequestSchema? Parse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            JsonNode? root = JsonNode.Parse(rawJson);
            return Parse(root?["params"]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ElicitationRequestSchema? Parse(JsonNode? parameters)
    {
        if (parameters is not JsonObject parameterObject)
        {
            return null;
        }

        string mode = GetString(parameterObject["mode"]) ?? "form";
        string? message = GetString(parameterObject["message"]) ?? GetString(parameterObject["prompt"]);
        JsonObject? requestedSchema = parameterObject["requestedSchema"] as JsonObject;
        if (requestedSchema is null)
        {
            return null;
        }

        JsonObject? properties = requestedSchema["properties"] as JsonObject;
        if (properties is null || properties.Count == 0)
        {
            return new ElicitationRequestSchema(message, mode, []);
        }

        HashSet<string> required = [];
        if (requestedSchema["required"] is JsonArray requiredArray)
        {
            foreach (JsonNode? item in requiredArray)
            {
                string? name = GetString(item);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    required.Add(name);
                }
            }
        }

        List<ElicitationFieldDefinition> fields = [];
        foreach ((string key, JsonNode? valueNode) in properties)
        {
            if (valueNode is not JsonObject fieldObject)
            {
                continue;
            }

            ElicitationFieldDefinition? field = BuildField(key, fieldObject, required.Contains(key));
            if (field is not null)
            {
                fields.Add(field);
            }
        }

        return new ElicitationRequestSchema(message, mode, fields);
    }

    private static ElicitationFieldDefinition? BuildField(string name, JsonObject fieldObject, bool isRequired)
    {
        string? type = GetString(fieldObject["type"]);
        string label = GetString(fieldObject["title"]) ?? FriendlyLabel(name);
        string? description = GetString(fieldObject["description"]);

        if (fieldObject["enum"] is JsonArray enumArray && enumArray.Count > 0)
        {
            List<ElicitationFieldOption> options = [];
            JsonArray? enumNames = fieldObject["enumNames"] as JsonArray;
            JsonArray? oneOf = fieldObject["oneOf"] as JsonArray;

            for (int i = 0; i < enumArray.Count; i++)
            {
                string? value = GetString(enumArray[i]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string optionLabel = value;
                if (enumNames is not null && i < enumNames.Count)
                {
                    optionLabel = GetString(enumNames[i]) ?? optionLabel;
                }
                else if (oneOf is not null && i < oneOf.Count && oneOf[i] is JsonObject oneOfObject)
                {
                    optionLabel = GetString(oneOfObject["title"]) ?? optionLabel;
                }

                options.Add(new ElicitationFieldOption(value, optionLabel));
            }

            return new ElicitationFieldDefinition(
                name,
                ElicitationFieldKind.Enum,
                label,
                description,
                isRequired,
                DefaultBoolean: null,
                DefaultText: GetString(fieldObject["default"]),
                DefaultNumber: null,
                options);
        }

        if (string.Equals(type, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            return new ElicitationFieldDefinition(
                name,
                ElicitationFieldKind.Boolean,
                label,
                description,
                isRequired,
                DefaultBoolean: GetBoolean(fieldObject["default"]),
                DefaultText: null,
                DefaultNumber: null,
                Options: []);
        }

        if (string.Equals(type, "number", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "integer", StringComparison.OrdinalIgnoreCase))
        {
            return new ElicitationFieldDefinition(
                name,
                ElicitationFieldKind.Number,
                label,
                description,
                isRequired,
                DefaultBoolean: null,
                DefaultText: null,
                DefaultNumber: GetDecimal(fieldObject["default"]),
                Options: []);
        }

        if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(type))
        {
            return new ElicitationFieldDefinition(
                name,
                ElicitationFieldKind.String,
                label,
                description,
                isRequired,
                DefaultBoolean: null,
                DefaultText: GetString(fieldObject["default"]),
                DefaultNumber: null,
                Options: []);
        }

        return null;
    }

    private static string FriendlyLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        List<char> chars = [];
        for (int i = 0; i < key.Length; i++)
        {
            char ch = key[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLetterOrDigit(key[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(i == 0 ? char.ToUpperInvariant(ch) : ch);
        }

        return new string([.. chars]);
    }

    private static string? GetString(JsonNode? node)
    {
        return node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? value)
            ? value
            : null;
    }

    private static bool? GetBoolean(JsonNode? node)
    {
        return node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out bool value)
            ? value
            : null;
    }

    private static decimal? GetDecimal(JsonNode? node)
    {
        if (node is not JsonValue jsonValue)
        {
            return null;
        }

        if (jsonValue.TryGetValue<decimal>(out decimal decimalValue))
        {
            return decimalValue;
        }

        if (jsonValue.TryGetValue<double>(out double doubleValue))
        {
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
        }

        return null;
    }
}
