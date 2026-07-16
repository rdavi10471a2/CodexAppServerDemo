using System.Text;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;

namespace CodexAppServerBlazor.Services.Tasks;

public sealed class TaskWorkflowContextService : ITaskWorkflowContextService
{
    private const int MaxNotesCharacters = 3600;
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
        IReadOnlyList<WorkflowTaskEventRow> events = snapshot.Events
            .Where(taskEvent => taskEvent.TaskId.Equals(activeTask.Id, StringComparison.Ordinal))
            .OrderByDescending(taskEvent => taskEvent.CreatedAt)
            .Take(MaxEvents)
            .ToArray();

        StringBuilder builder = new();
        builder.AppendLine("Host-authoritative current task supplied by Coding Services:");
        builder.AppendLine($"- Active task: {FormatTaskLabel(activeTask.TaskNumber)} {activeTask.Name}");
        builder.AppendLine($"- Task id: {activeTask.Id}");
        builder.AppendLine($"- Short name: {activeTask.ShortName}");
        builder.AppendLine($"- State: {activeTask.StateName} ({activeTask.StateCode})");
        if (!string.IsNullOrWhiteSpace(activeTask.Description))
        {
            builder.AppendLine("- Description:");
            builder.AppendLine("```markdown");
            builder.AppendLine(Compact(activeTask.Description, 1600));
            builder.AppendLine("```");
        }
        builder.AppendLine($"- Task database: {repository.DatabasePath}");
        builder.AppendLine($"- Task memory root: {repository.TaskMemoryRoot}");
        builder.AppendLine($"- Watched solution: {settings.WatchedSolutionPath}");
        builder.AppendLine("- This Active task is the authoritative current task for this Work turn.");
        builder.AppendLine("- Do not substitute another task, prior task, inferred task, or selected-file interpretation.");
        builder.AppendLine("- If the user request appears inconsistent with this task, report the inconsistency and ask for clarification instead of switching tasks.");
        builder.AppendLine("- Durable workflow memory lives in user notes, agent notes, and task events.");
        builder.AppendLine("- Keep solution index context volatile: refresh digest/MCP summaries when code structure matters; do not treat indexed summaries as durable task memory.");
        builder.AppendLine("- Runtime artifacts under the watched workspace, including runtime\\watched-solutions\\..., workflow\\history, working, staged, metadata, and task-memory, are not authoritative proof that source work is complete.");
        builder.AppendLine("- There is no authoritative host task-file list for this turn. Use governed discovery to choose candidate files, then let the declared edit-session file set become the authoritative changed-file set.");
        AppendNoteSection(builder, "User notes", activeTask.NotesMarkdownPath, repository.ReadNotes(activeTask.NotesMarkdownPath));
        AppendNoteSection(builder, "Agent notes", activeTask.AgentNotesMarkdownPath, repository.ReadNotes(activeTask.AgentNotesMarkdownPath));
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
