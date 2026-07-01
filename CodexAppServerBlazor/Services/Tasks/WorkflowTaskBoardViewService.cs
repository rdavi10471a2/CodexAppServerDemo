using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;

namespace CodexAppServerBlazor.Services.Tasks;

public sealed class WorkflowTaskBoardViewService : IWorkflowTaskBoardViewService
{
    private readonly CodingServicesSettingsProvider settingsProvider;

    public WorkflowTaskBoardViewService(CodingServicesSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public TaskBoardViewModel GetBoard(string workspaceRoot, string? selectedTaskId)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        List<TaskBoardColumnViewModel> columns = snapshot.States
            .OrderBy(state => state.SortOrder)
            .Select(state => new TaskBoardColumnViewModel(
                state.Code,
                state.Name,
                state.IsTerminal,
                false,
                snapshot.Tasks
                    .Where(task => !task.IsArchived && task.StateCode.Equals(state.Code, StringComparison.Ordinal))
                    .Select(task => ToTaskViewModel(task, snapshot.Files))
                    .ToArray()))
            .ToList();
        columns.Add(new TaskBoardColumnViewModel(
            "Archived",
            "Archived",
            true,
            true,
            snapshot.Tasks
                .Where(task => task.IsArchived)
                .Select(task => ToTaskViewModel(task, snapshot.Files))
                .ToArray()));

        WorkflowTaskRow? selectedRow = SelectTask(snapshot.Tasks, selectedTaskId);
        TaskBoardTaskDetailViewModel? selectedTask = selectedRow is null
            ? null
            : ToDetailViewModel(repository, selectedRow, snapshot.Files, snapshot.Events);

        return new TaskBoardViewModel(
            settings.WatchedSolutionPath,
            repository.DatabasePath,
            repository.TaskMemoryRoot,
            columns,
            repository.ListArchivedDiscussions()
                .Select(row => new TaskBoardArchivedDiscussionViewModel(
                    row.Id,
                    row.Name,
                    row.MarkdownPath,
                    row.ThreadId,
                    row.Trigger,
                    row.TurnMode,
                    FormatDate(row.CreatedAt)))
                .ToArray(),
            selectedTask);
    }

    public TaskBoardTaskViewModel CreateTask(string workspaceRoot, string name, string? shortName, string? notesMarkdown)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.CreateTask(name, shortName, notesMarkdown);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel UpdateTaskDetails(string workspaceRoot, string taskId, string name, string? shortName)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.UpdateTaskDetails(taskId, name, shortName);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel MoveTask(string workspaceRoot, string taskId, string stateCode)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.MoveTask(taskId, stateCode);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel UpdateNotes(string workspaceRoot, string taskId, string notesMarkdown)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.UpdateNotes(taskId, notesMarkdown);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel ArchiveTask(string workspaceRoot, string taskId)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.ArchiveTask(taskId);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public TaskBoardTaskViewModel RestoreTask(string workspaceRoot, string taskId)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        WorkflowTaskRow row = repository.RestoreTask(taskId);
        WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
        return ToTaskViewModel(row, snapshot.Files);
    }

    public void AddFile(string workspaceRoot, string taskId, string relativePath, string? intent, string? fileRole)
    {
        CreateRepository(settingsProvider.GetSettings(workspaceRoot)).AddFile(taskId, relativePath, intent, fileRole);
    }

    public void AddComment(string workspaceRoot, string taskId, string message)
    {
        CreateRepository(settingsProvider.GetSettings(workspaceRoot)).AddComment(taskId, message);
    }

    public string ReadArchivedDiscussionContent(string workspaceRoot, string archivedDiscussionId)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowTaskBoardRepository repository = CreateRepository(settings);
        ArchivedDiscussionRow row = repository.ListArchivedDiscussions()
            .FirstOrDefault(candidate => candidate.Id.Equals(archivedDiscussionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Archived discussion was not found: " + archivedDiscussionId);
        string archiveRoot = Path.GetFullPath(Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "archived-discussions"));
        string archivePath = Path.GetFullPath(row.MarkdownPath);
        if (!IsPathWithinRoot(archivePath, archiveRoot))
        {
            throw new InvalidOperationException("Archived discussion file is outside the archived discussion store.");
        }

        if (!File.Exists(archivePath))
        {
            throw new InvalidOperationException("Archived discussion file is missing: " + archivePath);
        }

        return File.ReadAllText(archivePath);
    }

    private static WorkflowTaskBoardRepository CreateRepository(CodingServicesSettings settings)
    {
        return new WorkflowTaskBoardRepository(
            SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
            SystemDataPaths.GetDefaultTaskMemoryRoot(settings));
    }

    private static WorkflowTaskRow? SelectTask(IReadOnlyList<WorkflowTaskRow> tasks, string? selectedTaskId)
    {
        if (!string.IsNullOrWhiteSpace(selectedTaskId))
        {
            WorkflowTaskRow? selected = tasks.FirstOrDefault(task =>
                task.Id.Equals(selectedTaskId, StringComparison.Ordinal));
            if (selected is not null)
            {
                return selected;
            }
        }

        return tasks.FirstOrDefault(task => !task.IsArchived && task.StateCode.Equals("Active", StringComparison.Ordinal))
            ?? tasks.FirstOrDefault(task => !task.IsArchived)
            ?? tasks.FirstOrDefault();
    }

    private static TaskBoardTaskViewModel ToTaskViewModel(
        WorkflowTaskRow task,
        IReadOnlyList<WorkflowTaskFileRow> files)
    {
        return new TaskBoardTaskViewModel(
            task.Id,
            task.TaskNumber,
            FormatTaskLabel(task.TaskNumber),
            task.Name,
            task.ShortName,
            task.StateCode,
            task.StateName,
            task.IsArchived,
            FormatDate(task.UpdatedAt),
            files.Count(file => file.TaskId.Equals(task.Id, StringComparison.Ordinal)));
    }

    private static TaskBoardTaskDetailViewModel ToDetailViewModel(
        WorkflowTaskBoardRepository repository,
        WorkflowTaskRow task,
        IReadOnlyList<WorkflowTaskFileRow> files,
        IReadOnlyList<WorkflowTaskEventRow> events)
    {
        return new TaskBoardTaskDetailViewModel(
            task.Id,
            task.TaskNumber,
            FormatTaskLabel(task.TaskNumber),
            task.Name,
            task.ShortName,
            task.StateCode,
            task.StateName,
            task.IsArchived,
            FormatDate(task.CreatedAt),
            FormatDate(task.UpdatedAt),
            task.NotesMarkdownPath,
            repository.ReadNotes(task.NotesMarkdownPath),
            task.AgentNotesMarkdownPath,
            repository.ReadNotes(task.AgentNotesMarkdownPath),
            files
                .Where(file => file.TaskId.Equals(task.Id, StringComparison.Ordinal))
                .Select(file => new TaskBoardFileViewModel(
                    file.Id,
                    file.RelativePath,
                    file.Intent ?? string.Empty,
                    file.FileRole ?? string.Empty))
                .ToArray(),
            events
                .Where(taskEvent => taskEvent.TaskId.Equals(task.Id, StringComparison.Ordinal))
                .OrderByDescending(taskEvent => taskEvent.CreatedAt)
                .Take(80)
                .Select(taskEvent => new TaskBoardEventViewModel(
                    taskEvent.Id,
                    taskEvent.EventTypeCode,
                    taskEvent.EventTypeName,
                    taskEvent.Message ?? string.Empty,
                    FormatDate(taskEvent.CreatedAt)))
                .ToArray());
    }

    private static string FormatDate(DateTime value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatTaskLabel(int taskNumber)
    {
        return "TASK-" + taskNumber.ToString("0000");
    }

    private static bool IsPathWithinRoot(string candidatePath, string rootPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedCandidate.Equals(normalizedRoot, comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.AltDirectorySeparatorChar,
                comparison);
    }
}
