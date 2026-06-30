namespace CodexAppServerBlazor.Components.Pages.Home.Tasks;

public sealed record TaskCreateRequest(string Name, string? ShortName);

public sealed record TaskMoveRequest(string TaskId, string StateCode);

public sealed record TaskDetailsSaveRequest(
    string TaskId,
    string Name,
    string? ShortName,
    string StateCode,
    string NotesMarkdown);

public sealed record TaskFileAddRequest(string TaskId, string RelativePath, string? Intent, string? FileRole);
