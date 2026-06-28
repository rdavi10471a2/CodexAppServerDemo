namespace CodexAppServerWinForms.Mcp;

/// <summary>
/// Workspace metadata exposed through the local MCP boundary.
/// </summary>
public sealed class HarnessWorkspaceWorkflow
{
    private readonly WorkspaceState workspaceState;

    public HarnessWorkspaceWorkflow(WorkspaceState workspaceState)
    {
        this.workspaceState = workspaceState;
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
