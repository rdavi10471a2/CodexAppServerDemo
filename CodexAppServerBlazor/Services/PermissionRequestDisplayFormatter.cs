using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexAppServerBlazor.Services;

public static class PermissionRequestDisplayFormatter
{
    public static PermissionRequestDisplayModel Build(CodexPermissionRequest request)
    {
        JsonNode? root = TryParseJson(request.RawJson);
        JsonNode? parameters = root?["params"];

        if (LooksLikeMcpToolCall(parameters))
        {
            return BuildMcpToolCall(request, parameters);
        }

        if (request.Method.Equals("item/commandExecution/requestApproval", StringComparison.OrdinalIgnoreCase) ||
            request.Method.Equals("execCommandApproval", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCommandApproval(request, parameters);
        }

        if (request.Method.Equals("item/fileChange/requestApproval", StringComparison.OrdinalIgnoreCase) ||
            request.Method.Equals("applyPatchApproval", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFileChangeApproval(request, parameters);
        }

        if (request.Method.Equals("item/permissions/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPermissionApproval(request, parameters);
        }

        if (PermissionRequestService.IsElicitationRequest(request.Method))
        {
            return BuildElicitation(request, parameters);
        }

        return BuildGeneric(request, parameters);
    }

    private static PermissionRequestDisplayModel BuildMcpToolCall(CodexPermissionRequest request, JsonNode? parameters)
    {
        string server = GetStringValue(parameters?["server"])
            ?? GetStringValue(parameters?["serverName"])
            ?? GetStringValue(parameters?["namespace"])
            ?? "MCP";
        string tool = GetStringValue(parameters?["tool"])
            ?? GetStringValue(parameters?["toolName"])
            ?? GetStringValue(parameters?["name"])
            ?? request.Method;
        string summary = GetPreferredSummary(request, "Governed MCP tool call awaiting approval.");
        string? prompt = GetStringValue(parameters?["prompt"]) ?? GetStringValue(parameters?["reason"]);
        JsonNode? arguments = parameters?["arguments"] ?? parameters?["args"];

        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "Server", server, isCode: true);
        AddField(fields, "Tool", tool, isCode: true);

        foreach (PermissionRequestDisplayField field in ExtractArgumentFields(arguments))
        {
            fields.Add(field);
        }

        return new PermissionRequestDisplayModel(
            KindLabel: "MCP Tool Call",
            Headline: "Approve MCP tool call",
            Summary: summary,
            Fields: fields,
            DetailTitle: string.IsNullOrWhiteSpace(prompt) ? null : "Prompt",
            DetailText: prompt,
            ArgumentsTitle: arguments is null ? null : "Arguments",
            ArgumentsJson: arguments is null ? null : FormatJson(arguments),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildCommandApproval(CodexPermissionRequest request, JsonNode? parameters)
    {
        string commandText = FormatCommand(parameters?["command"]);
        string? cwd = GetStringValue(parameters?["cwd"]);
        string? reason = GetStringValue(parameters?["reason"]);
        string? callId = GetStringValue(parameters?["callId"]);
        string summary = BuildCommandSummary(commandText, cwd, reason);

        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "CWD", cwd, isCode: true);
        AddField(fields, "Call Id", callId, isCode: true);
        AddField(fields, "Reason", reason);

        return new PermissionRequestDisplayModel(
            KindLabel: "Command Approval",
            Headline: "Run command",
            Summary: summary,
            Fields: fields,
            DetailTitle: string.IsNullOrWhiteSpace(commandText) ? null : "Command",
            DetailText: commandText,
            ArgumentsTitle: parameters is null ? null : "Command request",
            ArgumentsJson: parameters is null ? null : FormatJson(parameters),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildFileChangeApproval(CodexPermissionRequest request, JsonNode? parameters)
    {
        string? reason = GetStringValue(parameters?["reason"]);
        string? patchId = GetStringValue(parameters?["patchId"])
            ?? GetStringValue(parameters?["itemId"])
            ?? GetStringValue(parameters?["callId"]);
        IReadOnlyList<string> paths = ExtractPaths(parameters);
        string summary = BuildFileChangeSummary(paths, reason);

        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "Patch Id", patchId, isCode: true);
        AddField(fields, "Reason", reason);
        if (paths.Count > 0)
        {
            AddField(fields, paths.Count == 1 ? "Target File" : "Target Files", string.Join(Environment.NewLine, paths), isCode: true);
        }

        return new PermissionRequestDisplayModel(
            KindLabel: "File Change Approval",
            Headline: paths.Count switch
            {
                0 => "Approve file change",
                1 => "Change " + GetLeafName(paths[0]),
                _ => "Approve " + paths.Count + " file changes"
            },
            Summary: summary,
            Fields: fields,
            DetailTitle: null,
            DetailText: null,
            ArgumentsTitle: parameters is null ? null : "Change request",
            ArgumentsJson: parameters is null ? null : FormatJson(parameters),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildPermissionApproval(CodexPermissionRequest request, JsonNode? parameters)
    {
        string? reason = GetStringValue(parameters?["reason"]);
        string? cwd = GetStringValue(parameters?["cwd"]);
        JsonNode? permissions = parameters?["permissions"];
        string permissionSummary = FormatPermissions(permissions);
        string summary = BuildPermissionSummary(permissionSummary, cwd, reason);

        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "Reason", reason);
        AddField(fields, "CWD", cwd, isCode: true);
        AddField(fields, "Requested Permissions", permissionSummary);

        return new PermissionRequestDisplayModel(
            KindLabel: "Permission Request",
            Headline: "Grant permissions",
            Summary: summary,
            Fields: fields,
            DetailTitle: null,
            DetailText: null,
            ArgumentsTitle: permissions is null ? null : "Requested permissions",
            ArgumentsJson: permissions is null ? null : FormatJson(permissions),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildElicitation(CodexPermissionRequest request, JsonNode? parameters)
    {
        string? prompt = GetStringValue(parameters?["prompt"])
            ?? GetStringValue(parameters?["message"])
            ?? GetStringValue(parameters?["reason"]);
        ElicitationRequestSchema? schema = ElicitationRequestSchemaParser.Parse(parameters);
        JsonObject? meta = parameters?["_meta"] as JsonObject;
        if (LooksLikeWrappedMcpApproval(meta))
        {
            return BuildWrappedMcpApproval(request, parameters, meta, prompt, schema);
        }

        string summary = BuildElicitationSummary(prompt, schema);

        List<PermissionRequestDisplayField> fields = [];
        if (schema is not null)
        {
            AddField(fields, "Mode", schema.Mode);
            if (schema.Fields.Count > 0)
            {
                AddField(fields, "Fields", string.Join(Environment.NewLine, schema.Fields.Select(field => field.Label)));
            }
        }

        foreach (PermissionRequestDisplayField field in ExtractShallowFields(parameters, maxFields: 6))
        {
            if (!field.Label.Equals("Requested Schema", StringComparison.OrdinalIgnoreCase))
            {
                fields.Add(field);
            }
        }

        return new PermissionRequestDisplayModel(
            KindLabel: "Agent Question",
            Headline: schema?.Fields.Count > 0 ? "Answer agent question" : "Agent needs guidance",
            Summary: summary,
            Fields: fields,
            DetailTitle: string.IsNullOrWhiteSpace(prompt) ? null : "Prompt",
            DetailText: prompt,
            ArgumentsTitle: parameters is null ? null : "Request details",
            ArgumentsJson: parameters is null ? null : FormatJson(parameters),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildWrappedMcpApproval(
        CodexPermissionRequest request,
        JsonNode? parameters,
        JsonObject meta,
        string? prompt,
        ElicitationRequestSchema? schema)
    {
        string server = GetStringValue(parameters?["serverName"]) ?? "MCP";
        string tool = GetStringValue(meta["tool_name"])
            ?? ExtractToolNameFromPrompt(prompt)
            ?? "unknown_tool";
        if (string.Equals(tool, "request_operator_confirmation", StringComparison.OrdinalIgnoreCase))
        {
            return BuildOperatorConfirmationApproval(request, server, tool, prompt, schema);
        }

        string? toolDescription = GetStringValue(meta["tool_description"]);
        string? persistence = FormatPersistence(meta["persist"]);
        string? paramDisplay = FormatToolParamsDisplay(meta["tool_params_display"]);

        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "Server", server, isCode: true);
        AddField(fields, "Tool", tool, isCode: true);
        AddField(fields, "Parameters", paramDisplay, isCode: true);
        if (schema is not null && schema.Fields.Count > 0)
        {
            AddField(fields, "Answer Fields", string.Join(Environment.NewLine, schema.Fields.Select(field => field.Label)));
        }

        string summary = "The agent wants to call MCP tool '" + tool + "' on server '" + server + "'.";
        if (!string.IsNullOrWhiteSpace(toolDescription))
        {
            summary += " " + toolDescription;
        }

        return new PermissionRequestDisplayModel(
            KindLabel: "MCP Tool Call",
            Headline: "Approve MCP tool call",
            Summary: summary,
            Fields: fields,
            DetailTitle: string.IsNullOrWhiteSpace(prompt) ? (string.IsNullOrWhiteSpace(toolDescription) ? null : "Tool description") : "Prompt",
            DetailText: string.IsNullOrWhiteSpace(prompt) ? toolDescription : prompt,
            ArgumentsTitle: BuildWrappedMcpArgumentsTitle(persistence, paramDisplay),
            ArgumentsJson: BuildWrappedMcpArgumentsJson(server, tool, persistence, paramDisplay),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildOperatorConfirmationApproval(
        CodexPermissionRequest request,
        string server,
        string tool,
        string? prompt,
        ElicitationRequestSchema? schema)
    {
        List<PermissionRequestDisplayField> fields = [];
        AddField(fields, "Answer Type", "Yes / No");
        AddField(fields, "Tool", tool, isCode: true);
        AddField(fields, "Server", server, isCode: true);

        ElicitationFieldDefinition? answerField = schema?.Fields.FirstOrDefault();
        AddField(fields, "Default", answerField?.DefaultBoolean is true ? "Yes" : "No");

        string summary = "Choose yes or no to continue the governed workflow.";

        return new PermissionRequestDisplayModel(
            KindLabel: "Agent Question",
            Headline: "Answer agent question",
            Summary: summary,
            Fields: fields,
            DetailTitle: string.IsNullOrWhiteSpace(prompt) ? null : "Prompt",
            DetailText: prompt,
            ArgumentsTitle: null,
            ArgumentsJson: null,
            RawJson: FormatRawJson(request.RawJson));
    }

    private static PermissionRequestDisplayModel BuildGeneric(CodexPermissionRequest request, JsonNode? parameters)
    {
        string summary = "Agent request '" + request.Method + "' is awaiting a response.";
        List<PermissionRequestDisplayField> fields = [];
        foreach (PermissionRequestDisplayField field in ExtractShallowFields(parameters, maxFields: 8))
        {
            fields.Add(field);
        }

        return new PermissionRequestDisplayModel(
            KindLabel: "Agent Request",
            Headline: request.Method,
            Summary: summary,
            Fields: fields,
            DetailTitle: null,
            DetailText: null,
            ArgumentsTitle: parameters is null ? null : "Request details",
            ArgumentsJson: parameters is null ? null : FormatJson(parameters),
            RawJson: FormatRawJson(request.RawJson));
    }

    private static bool LooksLikeMcpToolCall(JsonNode? parameters)
    {
        return parameters is not null
            && (parameters["tool"] is not null || parameters["toolName"] is not null)
            && (parameters["arguments"] is not null || parameters["args"] is not null);
    }

    private static bool LooksLikeWrappedMcpApproval(JsonObject? meta)
    {
        return string.Equals(GetStringValue(meta?["codex_approval_kind"]), "mcp_tool_call", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonNode? TryParseJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetPreferredSummary(CodexPermissionRequest request, string fallback)
    {
        return string.IsNullOrWhiteSpace(request.Summary)
            ? fallback
            : request.Summary.Trim();
    }

    private static string BuildCommandSummary(string commandText, string? cwd, string? reason)
    {
        string summary = string.IsNullOrWhiteSpace(commandText)
            ? "The agent wants to run a command."
            : "The agent wants to run this command: " + commandText;

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            summary += " Working directory: " + cwd + ".";
        }
        else
        {
            summary += ".";
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            summary += " Reason: " + reason + ".";
        }

        return summary;
    }

    private static string BuildFileChangeSummary(IReadOnlyList<string> paths, string? reason)
    {
        string summary = paths.Count switch
        {
            0 => "The agent wants to apply a file change.",
            1 => "The agent wants to change " + GetLeafName(paths[0]) + ".",
            _ => "The agent wants to change " + paths.Count + " files."
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            summary += " Reason: " + reason + ".";
        }

        return summary;
    }

    private static string BuildPermissionSummary(string permissionSummary, string? cwd, string? reason)
    {
        string summary = "The agent is requesting permissions";
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            summary += " for " + cwd;
        }

        summary += ". Requested: " + permissionSummary + ".";

        if (!string.IsNullOrWhiteSpace(reason))
        {
            summary += " Reason: " + reason + ".";
        }

        return summary;
    }

    private static string BuildElicitationSummary(string? prompt, ElicitationRequestSchema? schema)
    {
        if (schema is not null && schema.Fields.Count > 0)
        {
            string fieldSummary = schema.Fields.Count == 1
                ? "1 answer field"
                : schema.Fields.Count + " answer fields";
            return string.IsNullOrWhiteSpace(prompt)
                ? "The agent is asking a structured question with " + fieldSummary + "."
                : "The agent is asking a structured question: " + prompt;
        }

        return string.IsNullOrWhiteSpace(prompt)
            ? "The agent is asking for direction."
            : "The agent is asking: " + prompt;
    }

    private static string? ExtractToolNameFromPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        Match match = Regex.Match(prompt, "tool\\s+\"(?<tool>[^\"]+)\"", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["tool"].Value;
        }

        return null;
    }

    private static string? FormatPersistence(JsonNode? node)
    {
        if (node is not JsonArray array || array.Count == 0)
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonNode? item in array)
        {
            string? value = GetStringValue(item);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(FriendlyLabel(value));
            }
        }

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static string? FormatToolParamsDisplay(JsonNode? node)
    {
        if (node is JsonArray array && array.Count > 0)
        {
            List<string> values = [];
            foreach (JsonNode? item in array)
            {
                string? scalar = FormatScalar(item);
                if (!string.IsNullOrWhiteSpace(scalar))
                {
                    values.Add(scalar);
                }
            }

            if (values.Count > 0)
            {
                return string.Join(Environment.NewLine, values);
            }
        }

        return null;
    }

    private static string? BuildWrappedMcpArgumentsTitle(string? persistence, string? paramDisplay)
    {
        return string.IsNullOrWhiteSpace(persistence) && string.IsNullOrWhiteSpace(paramDisplay)
            ? null
            : "Approval details";
    }

    private static string? BuildWrappedMcpArgumentsJson(string server, string tool, string? persistence, string? paramDisplay)
    {
        JsonObject details = [];
        details["server"] = server;
        details["tool"] = tool;

        if (!string.IsNullOrWhiteSpace(persistence))
        {
            details["approvalScope"] = persistence;
        }

        if (!string.IsNullOrWhiteSpace(paramDisplay))
        {
            details["parametersDisplay"] = paramDisplay;
        }

        return details.Count == 2 && string.IsNullOrWhiteSpace(persistence) && string.IsNullOrWhiteSpace(paramDisplay)
            ? null
            : FormatJson(details);
    }

    private static void AddField(List<PermissionRequestDisplayField> fields, string label, string? value, bool isCode = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields.Add(new PermissionRequestDisplayField(label, value.Trim(), isCode));
    }

    private static IReadOnlyList<PermissionRequestDisplayField> ExtractArgumentFields(JsonNode? arguments)
    {
        if (arguments is not JsonObject argumentObject)
        {
            return [];
        }

        string[] preferredOrder =
        [
            "watchedFilePath",
            "path",
            "workingFilePath",
            "sessionId",
            "containingType",
            "symbolType",
            "namespaceName",
            "projectPath",
            "workspacePath",
            "name",
            "title"
        ];

        List<PermissionRequestDisplayField> fields = [];
        HashSet<string> emitted = new(StringComparer.Ordinal);

        foreach (string key in preferredOrder)
        {
            if (argumentObject[key] is null)
            {
                continue;
            }

            string? value = FormatScalar(argumentObject[key]);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            fields.Add(new PermissionRequestDisplayField(FriendlyLabel(key), value, LooksLikeCodeValue(key)));
            emitted.Add(key);
        }

        foreach ((string key, JsonNode? valueNode) in argumentObject)
        {
            if (emitted.Contains(key))
            {
                continue;
            }

            string? value = FormatScalar(valueNode);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            fields.Add(new PermissionRequestDisplayField(FriendlyLabel(key), value, LooksLikeCodeValue(key)));
            if (fields.Count >= 8)
            {
                break;
            }
        }

        return fields;
    }

    private static IReadOnlyList<PermissionRequestDisplayField> ExtractShallowFields(JsonNode? parameters, int maxFields)
    {
        if (parameters is not JsonObject parameterObject)
        {
            return [];
        }

        List<PermissionRequestDisplayField> fields = [];
        foreach ((string key, JsonNode? valueNode) in parameterObject)
        {
            string? value = FormatScalar(valueNode);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            fields.Add(new PermissionRequestDisplayField(FriendlyLabel(key), value, LooksLikeCodeValue(key)));
            if (fields.Count >= maxFields)
            {
                break;
            }
        }

        return fields;
    }

    private static IReadOnlyList<string> ExtractPaths(JsonNode? parameters)
    {
        List<string> paths = [];
        CollectPaths(parameters, paths);
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static void CollectPaths(JsonNode? node, List<string> paths)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string key, JsonNode? valueNode) in obj)
                {
                    if (key.Contains("path", StringComparison.OrdinalIgnoreCase) && valueNode is JsonValue)
                    {
                        string? value = GetStringValue(valueNode);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            paths.Add(value);
                        }
                    }

                    if (valueNode is JsonObject or JsonArray)
                    {
                        CollectPaths(valueNode, paths);
                    }
                }
                break;

            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (item is JsonObject or JsonArray)
                    {
                        CollectPaths(item, paths);
                        continue;
                    }

                    string? value = GetStringValue(item);
                    if (!string.IsNullOrWhiteSpace(value) &&
                        (value.Contains("\\", StringComparison.Ordinal) || value.Contains("/", StringComparison.Ordinal)))
                    {
                        paths.Add(value);
                    }
                }
                break;
        }
    }

    private static string FormatPermissions(JsonNode? permissions)
    {
        if (permissions is not JsonObject permissionObject || permissionObject.Count == 0)
        {
            return "No explicit permission payload";
        }

        List<string> items = [];
        foreach ((string key, JsonNode? valueNode) in permissionObject)
        {
            if (valueNode is JsonObject detailObject)
            {
                string state = detailObject["enabled"] is JsonValue enabledValue && enabledValue.TryGetValue<bool>(out bool enabled)
                    ? enabled ? "enabled" : "disabled"
                    : "requested";
                items.Add(key + " (" + state + ")");
                continue;
            }

            items.Add(key);
        }

        return string.Join(", ", items);
    }

    private static string FormatCommand(JsonNode? commandNode)
    {
        if (commandNode is null)
        {
            return string.Empty;
        }

        if (commandNode is JsonValue)
        {
            return GetStringValue(commandNode) ?? string.Empty;
        }

        if (commandNode is JsonArray commandArray)
        {
            return string.Join(" ", commandArray.Select(item => QuoteIfNeeded(GetStringValue(item) ?? item?.ToJsonString() ?? string.Empty)));
        }

        return commandNode.ToJsonString();
    }

    private static string? FormatScalar(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out string? stringValue))
            {
                return stringValue;
            }

            return node.ToJsonString();
        }

        return null;
    }

    private static string FormatJson(JsonNode node)
    {
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatRawJson(string rawJson)
    {
        JsonNode? node = TryParseJson(rawJson);
        return node is null ? rawJson : FormatJson(node);
    }

    private static string? GetStringValue(JsonNode? node)
    {
        return node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? value)
            ? value
            : null;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return value.Contains(' ', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string FriendlyLabel(string key)
    {
        StringBuilder builder = new(key.Length + 8);
        for (int i = 0; i < key.Length; i++)
        {
            char ch = key[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLetterOrDigit(key[i - 1]))
            {
                builder.Append(' ');
            }

            if (i == 0)
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool LooksLikeCodeValue(string key)
    {
        return key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("id", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("type", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("namespace", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLeafName(string path)
    {
        int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
    }
}

public sealed record PermissionRequestDisplayModel(
    string KindLabel,
    string Headline,
    string Summary,
    IReadOnlyList<PermissionRequestDisplayField> Fields,
    string? DetailTitle,
    string? DetailText,
    string? ArgumentsTitle,
    string? ArgumentsJson,
    string RawJson);

public sealed record PermissionRequestDisplayField(
    string Label,
    string Value,
    bool IsCode);
