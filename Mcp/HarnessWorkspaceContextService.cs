using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Workspace metadata exposed through the local MCP boundary.
/// </summary>
public sealed class HarnessWorkspaceContextService
{
    private readonly WorkspaceState workspaceState;
    private readonly SourceWorkspaceService sourceWorkspaceService;
    private readonly CodingServicesSettingsProvider settingsProvider;

    public HarnessWorkspaceContextService(
        WorkspaceState workspaceState,
        SourceWorkspaceService sourceWorkspaceService,
        CodingServicesSettingsProvider settingsProvider)
    {
        this.workspaceState = workspaceState;
        this.sourceWorkspaceService = sourceWorkspaceService;
        this.settingsProvider = settingsProvider;
    }

    public Task<WorkspaceResult> GetWorkspaceAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Task.FromResult(WorkspaceResult.Fail("No workspace CWD has been selected in the Blazor control surface."));
        }

        if (!Directory.Exists(repoRoot))
        {
            return Task.FromResult(WorkspaceResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot));
        }

        var info = new DirectoryInfo(repoRoot);
        return Task.FromResult(new WorkspaceResult(
            Success: true,
            RepoRoot: repoRoot,
            DirectoryName: info.Name,
            Error: null));
    }

    public Task<WatchedSolutionDigestResult> GetWatchedSolutionDigestAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Task.FromResult(WatchedSolutionDigestResult.Fail("No workspace CWD has been selected in the Blazor control surface."));
        }

        if (!Directory.Exists(repoRoot))
        {
            return Task.FromResult(WatchedSolutionDigestResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot));
        }

        SourceWorkspaceStructureSnapshot snapshot = sourceWorkspaceService.BuildProductStructureSnapshot(repoRoot, filter: null);
        string summaryHash = ComputeSummaryHash(snapshot);
        long summaryBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot).LongLength;
        bool ready = IsSnapshotReady(snapshot);

        return Task.FromResult(new WatchedSolutionDigestResult(
            Success: ready,
            RepoRoot: repoRoot,
            WorkspaceRoot: snapshot.WorkspaceRoot,
            WatchedSolutionPath: snapshot.WatchedSolutionPath,
            IndexDatabasePath: snapshot.IndexDatabasePath,
            FileCount: snapshot.FileCount,
            ProjectCount: snapshot.Tree.Count,
            SummaryBytes: summaryBytes,
            SummaryHash: summaryHash,
            Ready: ready,
            Message: snapshot.Message,
            Error: ready ? null : snapshot.Message));
    }

    public Task<WatchedSolutionSummaryResult> GetWatchedSolutionSummaryAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Task.FromResult(WatchedSolutionSummaryResult.Fail("No workspace CWD has been selected in the Blazor control surface."));
        }

        if (!Directory.Exists(repoRoot))
        {
            return Task.FromResult(WatchedSolutionSummaryResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot));
        }

        SourceWorkspaceStructureSnapshot snapshot = sourceWorkspaceService.BuildProductStructureSnapshot(repoRoot, filter: null);
        bool ready = IsSnapshotReady(snapshot);
        return Task.FromResult(new WatchedSolutionSummaryResult(
            Success: ready,
            RepoRoot: repoRoot,
            WorkspaceRoot: snapshot.WorkspaceRoot,
            WatchedSolutionPath: snapshot.WatchedSolutionPath,
            IndexDatabasePath: snapshot.IndexDatabasePath,
            FileCount: snapshot.FileCount,
            Ready: ready,
            Tree: snapshot.Tree,
            Message: snapshot.Message,
            Error: ready ? null : snapshot.Message));
    }

    public Task<WatchedSolutionSummaryResult> GetTestProjectSummaryAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Task.FromResult(WatchedSolutionSummaryResult.Fail("No workspace CWD has been selected in the Blazor control surface."));
        }

        if (!Directory.Exists(repoRoot))
        {
            return Task.FromResult(WatchedSolutionSummaryResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot));
        }

        SourceWorkspaceStructureSnapshot snapshot = sourceWorkspaceService.BuildTestProjectStructureSnapshot(repoRoot, filter: null);
        bool ready = IsSnapshotReady(snapshot);
        return Task.FromResult(new WatchedSolutionSummaryResult(
            Success: ready,
            RepoRoot: repoRoot,
            WorkspaceRoot: snapshot.WorkspaceRoot,
            WatchedSolutionPath: snapshot.WatchedSolutionPath,
            IndexDatabasePath: snapshot.IndexDatabasePath,
            FileCount: snapshot.FileCount,
            Ready: ready,
            Tree: snapshot.Tree,
            Message: snapshot.Message,
            Error: ready ? null : snapshot.Message));
    }

    public Task<CurrentTaskResult> GetCurrentTaskAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Task.FromResult(CurrentTaskResult.Fail("No workspace CWD has been selected in the Blazor control surface."));
        }

        if (!Directory.Exists(repoRoot))
        {
            return Task.FromResult(CurrentTaskResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot));
        }

        try
        {
            CodingServicesSettings settings = settingsProvider.GetSettings(repoRoot);
            WorkflowTaskBoardRepository repository = new(
                SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
                SystemDataPaths.GetDefaultTaskMemoryRoot(settings));
            WorkflowTaskBoardSnapshot snapshot = repository.LoadSnapshot();
            WorkflowTaskRow? activeTask = snapshot.Tasks.FirstOrDefault(task =>
                !task.IsArchived && task.StateCode.Equals("Active", StringComparison.Ordinal));

            if (activeTask is null)
            {
                return Task.FromResult(CurrentTaskResult.None(
                    repoRoot,
                    settings.WatchedSolutionPath,
                    repository.DatabasePath,
                    repository.TaskMemoryRoot,
                    "No Active task is currently set for the selected workspace."));
            }

            return Task.FromResult(new CurrentTaskResult(
                Success: true,
                RepoRoot: repoRoot,
                WatchedSolutionPath: settings.WatchedSolutionPath,
                TaskBoardDatabasePath: repository.DatabasePath,
                TaskMemoryRoot: repository.TaskMemoryRoot,
                HasCurrentTask: true,
                TaskId: activeTask.Id,
                TaskNumber: activeTask.TaskNumber,
                TaskLabel: FormatTaskLabel(activeTask.TaskNumber),
                TaskName: activeTask.Name,
                TaskDescription: activeTask.Description,
                TaskShortName: activeTask.ShortName,
                TaskStateCode: activeTask.StateCode,
                TaskStateName: activeTask.StateName,
                UserNotesPath: activeTask.NotesMarkdownPath,
                AgentNotesPath: activeTask.AgentNotesMarkdownPath,
                Error: null,
                Message: "Active task context is available for this workspace."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CurrentTaskResult.Fail(ex.Message, repoRoot));
        }
    }

    public async Task<ReindexWorkspaceResult> RebuildSolutionIndexAsync(CancellationToken cancellationToken)
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return ReindexWorkspaceResult.Fail("No workspace CWD has been selected in the Blazor control surface.");
        }

        if (!Directory.Exists(repoRoot))
        {
            return ReindexWorkspaceResult.Fail($"Workspace CWD no longer exists: {repoRoot}", repoRoot);
        }

        try
        {
            CodingServicesSettings settings = settingsProvider.GetSettings(repoRoot);
            await sourceWorkspaceService.RebuildIndexAsync(repoRoot, cancellationToken);

            SourceWorkspaceStructureSnapshot snapshot = sourceWorkspaceService.BuildProductStructureSnapshot(repoRoot, filter: null);
            bool ready = IsSnapshotReady(snapshot);
            return new ReindexWorkspaceResult(
                Success: ready,
                RepoRoot: repoRoot,
                WorkspaceRoot: snapshot.WorkspaceRoot,
                WatchedSolutionPath: snapshot.WatchedSolutionPath,
                IndexDatabasePath: snapshot.IndexDatabasePath,
                FileCount: snapshot.FileCount,
                ProjectCount: snapshot.Tree.Count,
                Ready: ready,
                Message: ready
                    ? "Watched solution index rebuild completed successfully."
                    : snapshot.Message,
                Error: ready ? null : snapshot.Message);
        }
        catch (Exception ex)
        {
            return ReindexWorkspaceResult.Fail(ex.Message, repoRoot);
        }
    }

    private static bool IsSnapshotReady(SourceWorkspaceStructureSnapshot snapshot)
    {
        return File.Exists(snapshot.WatchedSolutionPath)
            && File.Exists(snapshot.IndexDatabasePath)
            && snapshot.FileCount > 0
            && snapshot.Tree.Count > 0
            && string.IsNullOrWhiteSpace(snapshot.Message);
    }

    private static string ComputeSummaryHash(SourceWorkspaceStructureSnapshot snapshot)
    {
        byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatTaskLabel(int taskNumber)
    {
        return "TASK-" + taskNumber.ToString("0000");
    }
}

public sealed record WorkspaceResult(
    bool Success,
    string? RepoRoot,
    string? DirectoryName,
    string? Error)
{
    public static WorkspaceResult Fail(string error, string? repoRoot = null)
    {
        return new WorkspaceResult(
            Success: false,
            RepoRoot: repoRoot,
            DirectoryName: null,
            Error: error);
    }
}

public sealed record WatchedSolutionSummaryResult(
    bool Success,
    string? RepoRoot,
    string? WorkspaceRoot,
    string? WatchedSolutionPath,
    string? IndexDatabasePath,
    int FileCount,
    bool Ready,
    IReadOnlyList<SourceTreeNode> Tree,
    string? Message,
    string? Error)
{
    public static WatchedSolutionSummaryResult Fail(string error, string? repoRoot = null)
    {
        return new WatchedSolutionSummaryResult(
            Success: false,
            RepoRoot: repoRoot,
            WorkspaceRoot: null,
            WatchedSolutionPath: null,
            IndexDatabasePath: null,
            FileCount: 0,
            Ready: false,
            Tree: [],
            Message: null,
            Error: error);
    }
}

public sealed record WatchedSolutionDigestResult(
    bool Success,
    string? RepoRoot,
    string? WorkspaceRoot,
    string? WatchedSolutionPath,
    string? IndexDatabasePath,
    int FileCount,
    int ProjectCount,
    long SummaryBytes,
    string? SummaryHash,
    bool Ready,
    string? Message,
    string? Error)
{
    public static WatchedSolutionDigestResult Fail(string error, string? repoRoot = null)
    {
        return new WatchedSolutionDigestResult(
            Success: false,
            RepoRoot: repoRoot,
            WorkspaceRoot: null,
            WatchedSolutionPath: null,
            IndexDatabasePath: null,
            FileCount: 0,
            ProjectCount: 0,
            SummaryBytes: 0,
            SummaryHash: null,
            Ready: false,
            Message: null,
            Error: error);
    }
}

public sealed record CurrentTaskResult(
    bool Success,
    string? RepoRoot,
    string? WatchedSolutionPath,
    string? TaskBoardDatabasePath,
    string? TaskMemoryRoot,
    bool HasCurrentTask,
    string? TaskId,
    int? TaskNumber,
    string? TaskLabel,
    string? TaskName,
    string? TaskDescription,
    string? TaskShortName,
    string? TaskStateCode,
    string? TaskStateName,
    string? UserNotesPath,
    string? AgentNotesPath,
    string? Error,
    string? Message)
{
    public static CurrentTaskResult Fail(string error, string? repoRoot = null)
    {
        return new CurrentTaskResult(
            Success: false,
            RepoRoot: repoRoot,
            WatchedSolutionPath: null,
            TaskBoardDatabasePath: null,
            TaskMemoryRoot: null,
            HasCurrentTask: false,
            TaskId: null,
            TaskNumber: null,
            TaskLabel: null,
            TaskName: null,
            TaskDescription: null,
            TaskShortName: null,
            TaskStateCode: null,
            TaskStateName: null,
            UserNotesPath: null,
            AgentNotesPath: null,
            Error: error,
            Message: null);
    }

    public static CurrentTaskResult None(
        string repoRoot,
        string? watchedSolutionPath,
        string taskBoardDatabasePath,
        string taskMemoryRoot,
        string message)
    {
        return new CurrentTaskResult(
            Success: true,
            RepoRoot: repoRoot,
            WatchedSolutionPath: watchedSolutionPath,
            TaskBoardDatabasePath: taskBoardDatabasePath,
            TaskMemoryRoot: taskMemoryRoot,
            HasCurrentTask: false,
            TaskId: null,
            TaskNumber: null,
            TaskLabel: null,
            TaskName: null,
            TaskDescription: null,
            TaskShortName: null,
            TaskStateCode: null,
            TaskStateName: null,
            UserNotesPath: null,
            AgentNotesPath: null,
            Error: null,
            Message: message);
    }
}

public sealed record ReindexWorkspaceResult(
    bool Success,
    string? RepoRoot,
    string? WorkspaceRoot,
    string? WatchedSolutionPath,
    string? IndexDatabasePath,
    int FileCount,
    int ProjectCount,
    bool Ready,
    string? Message,
    string? Error)
{
    public static ReindexWorkspaceResult Fail(string error, string? repoRoot = null)
    {
        return new ReindexWorkspaceResult(
            Success: false,
            RepoRoot: repoRoot,
            WorkspaceRoot: null,
            WatchedSolutionPath: null,
            IndexDatabasePath: null,
            FileCount: 0,
            ProjectCount: 0,
            Ready: false,
            Message: null,
            Error: error);
    }
}
