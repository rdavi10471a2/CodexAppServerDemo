using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class WorkspaceMcpTools
{
    private readonly HarnessWorkspaceWorkflow workflow;

    public WorkspaceMcpTools(HarnessWorkspaceWorkflow workflow)
    {
        this.workflow = workflow;
    }

    [McpServerTool]
    [Description("Returns metadata for the workspace CWD selected in the Coding Services Blazor control surface.")]
    public Task<WorkspaceResult> GetWorkspace(CancellationToken cancellationToken = default)
    {
        return workflow.GetWorkspaceAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns cheap watched-solution readiness metadata, counts, and a summary hash so an agent can decide whether to reload deeper context.")]
    public Task<WatchedSolutionDigestResult> GetWatchedSolutionDigest(CancellationToken cancellationToken = default)
    {
        return workflow.GetWatchedSolutionDigestAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the indexed watched-solution project/file/type/member tree for on-demand agent discovery. Does not include source file bodies.")]
    public Task<WatchedSolutionSummaryResult> GetWatchedSolutionSummary(CancellationToken cancellationToken = default)
    {
        return workflow.GetWatchedSolutionSummaryAsync(cancellationToken);
    }
}
