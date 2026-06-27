using System.Text.Json.Nodes;

namespace CodexAppServerWinForms;

public sealed record JsonRpcResponse(int? Id, JsonNode? Result, JsonNode? Error, string RawJson);
