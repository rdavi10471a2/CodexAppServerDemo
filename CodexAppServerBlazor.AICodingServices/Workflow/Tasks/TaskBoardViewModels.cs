namespace CodexAppServerBlazor.AICodingServices.Workflow.Tasks;

public sealed record TaskBoardViewModel(
    string WatchedSolutionPath,
    string DatabasePath,
    string TaskMemoryRoot,
    IReadOnlyList<TaskBoardColumnViewModel> Columns,
    TaskBoardTaskDetailViewModel? SelectedTask)
{
    public static TaskBoardViewModel Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        null);
}

public sealed record TaskBoardColumnViewModel(
    string StateCode,
    string StateName,
    bool IsTerminal,
    bool IsArchive,
    IReadOnlyList<TaskBoardTaskViewModel> Tasks);

public sealed record TaskBoardTaskViewModel(
    string Id,
    int TaskNumber,
    string TaskLabel,
    string Name,
    string ShortName,
    string StateCode,
    string StateName,
    bool IsArchived,
    string UpdatedLabel,
    int FileCount);

public sealed record TaskBoardTaskDetailViewModel(
    string Id,
    int TaskNumber,
    string TaskLabel,
    string Name,
    string ShortName,
    string StateCode,
    string StateName,
    bool IsArchived,
    string CreatedLabel,
    string UpdatedLabel,
    string? NotesMarkdownPath,
    string NotesMarkdown,
    string? AgentNotesMarkdownPath,
    string AgentNotesMarkdown,
    IReadOnlyList<TaskBoardFileViewModel> Files,
    IReadOnlyList<TaskBoardEventViewModel> Events);

public sealed record TaskBoardFileViewModel(
    string Id,
    string RelativePath,
    string Intent,
    string FileRole);

public sealed record TaskBoardEventViewModel(
    string Id,
    string EventTypeCode,
    string EventTypeName,
    string Message,
    string CreatedLabel);
