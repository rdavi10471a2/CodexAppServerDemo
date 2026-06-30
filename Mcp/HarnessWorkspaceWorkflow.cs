using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Workspace metadata exposed through the local MCP boundary.
/// </summary>
public sealed class HarnessWorkspaceWorkflow
{
    private readonly WorkspaceState workspaceState;
    private readonly SourceWorkspaceService sourceWorkspaceService;

    public HarnessWorkspaceWorkflow(WorkspaceState workspaceState, SourceWorkspaceService sourceWorkspaceService)
    {
        this.workspaceState = workspaceState;
        this.sourceWorkspaceService = sourceWorkspaceService;
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
