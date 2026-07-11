using System.ComponentModel;
using CodexAppServerBlazor.Services.Workflow;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class WorkspaceReviewMcpTools
{
    private readonly HarnessWorkspaceReviewService workspaceReviewService;

    public WorkspaceReviewMcpTools(HarnessWorkspaceReviewService workspaceReviewService)
    {
        this.workspaceReviewService = workspaceReviewService;
    }

    [McpServerTool]
    [Description("Lists pending staged review records for the currently selected workspace. Use this before accepting or rejecting staged changes.")]
    public Task<IReadOnlyList<StagedReviewQueueItem>> ListPendingStagedReviews(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceReviewService.ListPending());
    }

    [McpServerTool]
    [Description("Loads the staged review model for a specific staged record id in the currently selected workspace.")]
    public Task<StagedReviewPageModel> LoadStagedReview(string stagedRecordId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceReviewService.Load(stagedRecordId));
    }

    [McpServerTool]
    [Description("Loads the next pending staged review model for a staged-edit session id in the currently selected workspace.")]
    public Task<StagedReviewPageModel> LoadNextSessionReview(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceReviewService.LoadNextForSession(sessionId));
    }

    [McpServerTool]
    [Description("Stages every file declared for a governed edit session, records pre-merge validation for each file, and raises one governed review elicitation for the whole session. This is the only governed staging path, including queue-of-one single-file sessions.")]
    public Task<StageForReviewResult> StageEditSessionForReview(
        McpServer server,
        string sessionId,
        string? ledgerSummary = null,
        CancellationToken cancellationToken = default)
    {
        return workspaceReviewService.StageEditSessionForReviewAsync(
            new McpServerReviewElicitor(server),
            sessionId,
            ledgerSummary,
            cancellationToken);
    }

    [McpServerTool]
    [Description("Accepts a staged review record into watched source, records the workflow decision, and runs post-accept refresh behavior.")]
    public Task<StagedReviewPageActionResult> AcceptStagedReview(
        string stagedRecordId,
        bool forceApproveValidation = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceReviewService.Accept(stagedRecordId, forceApproveValidation));
    }

    [McpServerTool]
    [Description("Rejects a staged review record, leaves watched source unchanged, and records the workflow decision.")]
    public Task<StagedReviewPageActionResult> RejectStagedReview(string stagedRecordId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workspaceReviewService.Reject(stagedRecordId));
    }
}
