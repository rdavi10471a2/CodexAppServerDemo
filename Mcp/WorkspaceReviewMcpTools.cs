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
    public IReadOnlyList<StagedReviewQueueItem> ListPendingStagedReviews()
    {
        return workspaceReviewService.ListPending();
    }

    [McpServerTool]
    [Description("Loads the staged review model for a specific staged record id in the currently selected workspace.")]
    public StagedReviewPageModel LoadStagedReview(string stagedRecordId)
    {
        return workspaceReviewService.Load(stagedRecordId);
    }

    [McpServerTool]
    [Description("Loads the next pending staged review model for a staged-edit session id in the currently selected workspace.")]
    public StagedReviewPageModel LoadNextSessionReview(string sessionId)
    {
        return workspaceReviewService.LoadNextForSession(sessionId);
    }

    [McpServerTool]
    [Description("Stages the current governed Working candidate for a workspace file into review, records pre-merge validation, and queues the host's modal review flow. The returned review URL is diagnostic only and should not replace the in-app governed review boundary.")]
    public StageForReviewResult StageCurrentCandidateForReview(
        string watchedFilePath,
        string? sessionId = null,
        string? ledgerSummary = null)
    {
        return workspaceReviewService.StageCurrentCandidateForReview(watchedFilePath, sessionId, ledgerSummary);
    }

    [McpServerTool]
    [Description("Accepts a staged review record into watched source, records the workflow decision, and runs post-accept refresh behavior.")]
    public StagedReviewPageActionResult AcceptStagedReview(string stagedRecordId, bool forceApproveValidation = false)
    {
        return workspaceReviewService.Accept(stagedRecordId, forceApproveValidation);
    }

    [McpServerTool]
    [Description("Rejects a staged review record, leaves watched source unchanged, and records the workflow decision.")]
    public StagedReviewPageActionResult RejectStagedReview(string stagedRecordId)
    {
        return workspaceReviewService.Reject(stagedRecordId);
    }
}
