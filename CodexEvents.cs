namespace CodexAppServerBlazor;

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
    string? Detail,
    string? CorrelationId,
    string? ApprovalId = null);

public sealed record StatusEvent(
    string EventType,
    string Summary);

public enum CodexTurnAttachmentKind
{
    LocalImage,
    Text
}

public sealed record CodexTurnAttachment(
    string Name,
    string Path,
    CodexTurnAttachmentKind Kind,
    long SizeBytes);

public sealed record CodexServerRequestEvent(
    int RequestId,
    string Method,
    string Summary,
    string RawJson,
    string? CorrelationId,
    string? ApprovalId);
