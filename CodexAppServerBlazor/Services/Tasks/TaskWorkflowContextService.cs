using System.Text;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;

namespace CodexAppServerBlazor.Services.Tasks;

public sealed class TaskWorkflowContextService : ITaskWorkflowContextService
{
    private const int MaxNotesCharacters = 3600;
    private const int MaxFiles = 30;
    private const int MaxEvents = 12;

    private readonly CodingServicesSettingsProvider settingsProvider;

    public TaskWorkflowContextService(CodingServicesSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public WorkflowTurnTaskContext BuildTurnContext(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return new WorkflowTurnTaskContext(null, "Task context skipped because the CWD is missing.", null);
        }

        try
        {
            CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
            WorkflowTaskBoardRepository repository = CreateRepository(settings);
            WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
            WorkflowTaskRow? activeTask = snapshot.Tasks.FirstOrDefault(task =>
                !task.IsArchived && task.StateCode.Equals("Active", StringComparison.Ordinal));
            if (activeTask is null)
            {
                return new WorkflowTurnTaskContext(
                    null,
                    "Task context skipped because no Active task is set.",
                    null);
            }

            string prompt = BuildPrompt(repository, snapshot, activeTask, settings);
            string status = "Attached active task context for "
                + FormatTaskLabel(activeTask.TaskNumber)
                + " "
                + activeTask.ShortName
                + ".";

            return new WorkflowTurnTaskContext(prompt, status, activeTask.Id);
        }
        catch (Exception ex)
        {
            return new WorkflowTurnTaskContext(
                null,
                "Task context skipped: " + ex.Message,
                null);
        }
    }

    private static WorkflowTaskBoardRepository CreateRepository(CodingServicesSettings settings)
    {
        return new WorkflowTaskBoardRepository(
            SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
            SystemDataPaths.GetDefaultTaskMemoryRoot(settings));
    }

    private static string BuildPrompt(
        WorkflowTaskBoardRepository repository,
        WorkflowTaskBoardSnapshot snapshot,
        WorkflowTaskRow activeTask,
        CodingServicesSettings settings)
    {
        IReadOnlyList<WorkflowTaskFileRow> files = snapshot.Files
            .Where(file => file.TaskId.Equals(activeTask.Id, StringComparison.Ordinal))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToArray();
        int totalFileCount = snapshot.Files.Count(file => file.TaskId.Equals(activeTask.Id, StringComparison.Ordinal));
        IReadOnlyList<WorkflowTaskEventRow> events = snapshot.Events
            .Where(taskEvent => taskEvent.TaskId.Equals(activeTask.Id, StringComparison.Ordinal))
            .OrderByDescending(taskEvent => taskEvent.CreatedAt)
            .Take(MaxEvents)
            .ToArray();

        StringBuilder builder = new();
        builder.AppendLine("Active task context supplied by Coding Services:");
        builder.AppendLine($"- Active task: {FormatTaskLabel(activeTask.TaskNumber)} {activeTask.Name}");
        builder.AppendLine($"- Task id: {activeTask.Id}");
        builder.AppendLine($"- Short name: {activeTask.ShortName}");
        builder.AppendLine($"- State: {activeTask.StateName} ({activeTask.StateCode})");
        builder.AppendLine($"- Task database: {repository.DatabasePath}");
        builder.AppendLine($"- Task memory root: {repository.TaskMemoryRoot}");
        builder.AppendLine($"- Watched solution: {settings.WatchedSolutionPath}");
        builder.AppendLine("- Active task is the current workflow pointer for this turn.");
        builder.AppendLine("- Durable workflow memory lives in user notes, agent notes, task files, and task events.");
        builder.AppendLine("- Keep solution index context volatile: refresh digest/MCP summaries when code structure matters; do not treat indexed summaries as durable task memory.");
        builder.AppendLine("- At turn completion, ask whether agent notes should be updated if the outcome changes durable workflow memory.");
        AppendNoteSection(builder, "User notes", activeTask.NotesMarkdownPath, repository.ReadNotes(activeTask.NotesMarkdownPath));
        AppendNoteSection(builder, "Agent notes", activeTask.AgentNotesMarkdownPath, repository.ReadNotes(activeTask.AgentNotesMarkdownPath));
        AppendFiles(builder, files, totalFileCount);
        AppendEvents(builder, events);
        return builder.ToString();
    }

    private static void AppendNoteSection(
        StringBuilder builder,
        string title,
        string? path,
        string markdown)
    {
        builder.AppendLine(title + ":");
        if (string.IsNullOrWhiteSpace(path))
        {
            builder.AppendLine("- Path: not set");
        }
        else
        {
            builder.AppendLine("- Path: " + path);
        }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            builder.AppendLine("- Content: empty");
            return;
        }

        builder.AppendLine("```markdown");
        builder.AppendLine(Compact(markdown, MaxNotesCharacters));
        builder.AppendLine("```");
    }

    private static void AppendFiles(
        StringBuilder builder,
        IReadOnlyList<WorkflowTaskFileRow> files,
        int totalFileCount)
    {
        builder.AppendLine("Task files:");
        if (files.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (WorkflowTaskFileRow file in files)
        {
            string detail = string.Join(
                "; ",
                new[] { file.Intent, file.FileRole }.Where(value => !string.IsNullOrWhiteSpace(value)));
            builder.AppendLine(string.IsNullOrWhiteSpace(detail)
                ? "- " + file.RelativePath
                : "- " + file.RelativePath + " (" + detail + ")");
        }

        if (totalFileCount > files.Count)
        {
            builder.AppendLine("- ... " + (totalFileCount - files.Count) + " more task file(s) omitted.");
        }
    }

    private static void AppendEvents(StringBuilder builder, IReadOnlyList<WorkflowTaskEventRow> events)
    {
        builder.AppendLine("Recent task events:");
        if (events.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (WorkflowTaskEventRow taskEvent in events)
        {
            string message = string.IsNullOrWhiteSpace(taskEvent.Message)
                ? taskEvent.EventTypeName
                : taskEvent.Message.Trim();
            builder.AppendLine("- "
                + taskEvent.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                + " "
                + taskEvent.EventTypeName
                + ": "
                + CompactLine(message, 240));
        }
    }

    private static string Compact(string value, int maxCharacters)
    {
        string normalized = value.Replace("\r\n", "\n").Trim();
        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..maxCharacters].TrimEnd()
            + Environment.NewLine
            + "... truncated "
            + (normalized.Length - maxCharacters)
            + " character(s).";
    }

    private static string CompactLine(string value, int maxCharacters)
    {
        string normalized = string.Join(" ", value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..maxCharacters].TrimEnd() + "...";
    }

    private static string FormatTaskLabel(int taskNumber)
    {
        return "TASK-" + taskNumber.ToString("0000");
    }
}
