using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class WorkspaceMcpTools
{
    private readonly HarnessWorkspaceContextService workspaceContextService;

    public WorkspaceMcpTools(HarnessWorkspaceContextService workspaceContextService)
    {
        this.workspaceContextService = workspaceContextService;
    }

    [McpServerTool]
    [Description("Returns metadata for the workspace CWD selected in the Coding Services Blazor control surface.")]
    public Task<WorkspaceResult> GetWorkspace(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWorkspaceAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns cheap watched-solution readiness metadata, counts, and a summary hash so an agent can decide whether to reload deeper context.")]
    public Task<WatchedSolutionDigestResult> GetWatchedSolutionDigest(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWatchedSolutionDigestAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the indexed watched-solution project/file/type/member tree for on-demand agent discovery. Does not include source file bodies.")]
    public Task<WatchedSolutionSummaryResult> GetWatchedSolutionSummary(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWatchedSolutionSummaryAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the indexed project/file/type/member tree for configured test projects only. Does not include source file bodies.")]
    public Task<WatchedSolutionSummaryResult> GetTestProjectSummary(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetTestProjectSummaryAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the current Active task for the selected workspace, or reports that no current task is set.")]
    public Task<CurrentTaskResult> GetCurrentTask(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetCurrentTaskAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Rebuilds the watched solution index for the selected workspace and returns the refreshed readiness metadata.")]
    public Task<ReindexWorkspaceResult> RebuildSolutionIndex(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.RebuildSolutionIndexAsync(cancellationToken);
    }
}
