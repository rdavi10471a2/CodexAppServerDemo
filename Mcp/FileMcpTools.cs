using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodexAppServerWinForms.Mcp;

[McpServerToolType]
public sealed class FileMcpTools
{
    private readonly HarnessFileWorkflow _workflow;

    public FileMcpTools(HarnessFileWorkflow workflow)
    {
        _workflow = workflow;
    }

    [McpServerTool]
    [Description("Returns metadata and optionally content for the file currently selected in the Codex WinForms harness.")]
    public Task<SelectedFileResult> GetSelectedFile(
        [Description("Whether to include the selected file content. Use false for metadata only.")]
        bool includeContent = true,
        CancellationToken cancellationToken = default)
    {
        return _workflow.GetSelectedFileAsync(includeContent, cancellationToken);
    }
}
