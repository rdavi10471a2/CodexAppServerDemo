using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodexAppServerWinForms.Mcp;

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
}
