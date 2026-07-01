namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkflowSessionState
{
    public WorkflowSessionState(string workspaceRoot, WorkflowTurnMode initialMode)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        InitialMode = initialMode;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string WorkspaceRoot { get; }

    public WorkflowTurnMode InitialMode { get; }

    public DateTimeOffset StartedAt { get; }

    public bool HasAttachedWorkspaceContext { get; set; }
}
