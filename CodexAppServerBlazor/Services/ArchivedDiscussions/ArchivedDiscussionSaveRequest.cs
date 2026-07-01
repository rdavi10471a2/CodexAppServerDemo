namespace CodexAppServerBlazor.Services.ArchivedDiscussions;

public sealed record ArchivedDiscussionSaveRequest(
    string WorkspaceRoot,
    string Name,
    string? ThreadId,
    string TurnMode,
    string Trigger,
    string TranscriptText,
    string DraftText,
    IReadOnlyList<string> AttachmentNames,
    DateTimeOffset CapturedAt);
