namespace CodexAppServerWinForms;

//public enum CodexEventKind
//{
//    Protocol,
//    Assistant,
//    Telemetry,
//    Tool,
//    Status,
//    Error
//}

public sealed record AssistantTextEvent(string Text, bool IsFinal);

public sealed record TelemetryEvent(
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    int? ReasoningOutputTokens,
    int? ModelContextWindow,
    int? PrimaryUsedPercent,
    int? SecondaryUsedPercent,
    string? PlanType,
    string Summary);

public sealed record ToolEvent(
    string EventType,
    string Name,
    string? Status,
    string? Detail);

public sealed record StatusEvent(
    string EventType,
    string Summary);
