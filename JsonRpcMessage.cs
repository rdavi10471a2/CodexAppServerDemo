using System.Text.Json.Nodes;

namespace CodexAppServerBlazor;

public sealed record JsonRpcResponse(int? Id, JsonNode? Result, JsonNode? Error, string RawJson);
