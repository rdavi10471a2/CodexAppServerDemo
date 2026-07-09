using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Staged review operations exposed through the local MCP boundary for the currently selected workspace.
/// The governed accept/reject gate is driven by MCP elicitation (see <see cref="IReviewElicitor"/>): staging
/// a candidate blocks until the operator answers the elicitation, and the answer maps directly to
/// accept/reject. This replaces the earlier out-of-band GovernedReviewCoordinator HTTP-hold, which could not
/// reliably suspend the agent turn.
/// </summary>
public sealed class HarnessWorkspaceReviewService
{
    private readonly WorkspaceState workspaceState;
    private readonly CodingServicesSettingsProvider settingsProvider;

    public HarnessWorkspaceReviewService(
        WorkspaceState workspaceState,
        CodingServicesSettingsProvider settingsProvider)
    {
        this.workspaceState = workspaceState;
        this.settingsProvider = settingsProvider;
    }

    public IReadOnlyList<StagedReviewQueueItem> ListPending()
    {
        return CreateReviewService().ListPending(GetWorkspaceRoot());
    }

    public StagedReviewPageModel Load(string stagedRecordId)
    {
        return CreateReviewService().Load(GetWorkspaceRoot(), stagedRecordId);
    }

    public StagedReviewPageModel LoadNextForSession(string sessionId)
    {
        return CreateReviewService().LoadNextForSession(GetWorkspaceRoot(), sessionId);
    }

    public StagedReviewPageActionResult Accept(string stagedRecordId, bool forceApproveValidation = false)
    {
        return CreateReviewService().Accept(GetWorkspaceRoot(), stagedRecordId, forceApproveValidation);
    }

    public StagedReviewPageActionResult Reject(string stagedRecordId)
    {
        return CreateReviewService().Reject(GetWorkspaceRoot(), stagedRecordId);
    }

    public async Task<StageForReviewResult> StageCurrentCandidateForReviewAsync(
        IReviewElicitor elicitor,
        string watchedFilePath,
        string? sessionId = null,
        string? ledgerSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (elicitor is null)
        {
            throw new ArgumentNullException(nameof(elicitor));
        }

        string workspaceRoot = GetWorkspaceRoot();
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowEditService workflowService = new(settings);
        IStagedReviewPageService reviewService = CreateReviewService();
        string fullWatchedPath = ResolveWatchedFilePath(workspaceRoot, watchedFilePath);
        EditSessionStatus editSession = workflowService.EnsureEditableSession(fullWatchedPath);
        string? resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? (!string.IsNullOrWhiteSpace(editSession.EditSessionId)
                ? editSession.EditSessionId
                : workspaceState.CurrentEditSessionId)
            : sessionId;
        if (string.IsNullOrWhiteSpace(resolvedSessionId))
        {
            throw new InvalidOperationException(
                $"No active governed edit session exists for '{editSession.RelativePath}'. Refresh the file through governed edit MCP first, then retry staging.");
        }

        StagedEditRecord record = workflowService.Stage(fullWatchedPath, ledgerSummary, resolvedSessionId);
        workspaceState.SetCurrentEditSessionId(record.SessionId);
        record = workflowService.PrepareReviewFileForLaunch(record.StagedRecordId);

        PreMergeValidationResult validation = new PreMergeValidationService().Validate(settings, record);
        record = workflowService.RecordPreMergeValidation(record.StagedRecordId, validation, forceApproved: false);

        string reviewUrl = BuildReviewUrl(workspaceRoot, record);
        string sessionLabel = string.IsNullOrWhiteSpace(record.SessionId) ? "single-file review" : $"edit session '{record.SessionId}'";
        string launchMessage = validation.IsError
            ? $"Governed review elicitation raised for {sessionLabel}, but pre-merge validation reported issues. ReviewUrl (diagnostic only): {reviewUrl}"
            : $"Governed review elicitation raised for {sessionLabel}. ReviewUrl (diagnostic only): {reviewUrl}";
        record = workflowService.RecordDiffLaunch(record.StagedRecordId, launched: true, launchMessage);

        int pendingCount = reviewService.ListPending(workspaceRoot)
            .Count(item => item.SessionId.Equals(record.SessionId, StringComparison.Ordinal));

        // The elicitation is the block. Approving it opens the host review dialog, which resolves every
        // staged file in the edit session (accept/reject per file) while this call stays suspended. The
        // decision here reflects whether the operator completed the review session, not a single file:
        // the per-file accept/reject was applied by the dialog, so we only report the resulting state.
        ReviewDecision decision = await elicitor.RequestDecisionAsync(
            new ReviewElicitationRequest(
                record.SessionId,
                record.RelativePath,
                sessionLabel,
                pendingCount,
                validation.IsError,
                validation.Status,
                validation.DiagnosticCount,
                reviewUrl),
            cancellationToken);

        if (decision == ReviewDecision.Cancelled)
        {
            throw new OperationCanceledException(
                $"Governed review cancelled for {sessionLabel}. Staged record '{record.StagedRecordId}' left pending.");
        }

        StagedEditRecord refreshedRecord = workflowService.GetStagedRecord(record.StagedRecordId);
        string completionMessage = decision == ReviewDecision.Accepted
            ? $"Governed review session completed for {sessionLabel} via the review dialog."
            : $"Governed review declined for {sessionLabel}; staged items left for the operator.";
        return new StageForReviewResult(
            workflowService.CreateSummary(refreshedRecord),
            validation,
            reviewUrl,
            completionMessage);
    }

    private IStagedReviewPageService CreateReviewService()
    {
        return new StagedReviewPageService(settingsProvider);
    }

    private string GetWorkspaceRoot()
    {
        string? repoRoot = workspaceState.RepoRoot;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("No workspace CWD has been selected in the Blazor control surface.");
        }

        if (!Directory.Exists(repoRoot))
        {
            throw new InvalidOperationException($"Workspace CWD no longer exists: {repoRoot}");
        }

        string fullRepoRoot = Path.GetFullPath(repoRoot);
        CodingServicesSettings unused = settingsProvider.GetSettings(fullRepoRoot);
        return fullRepoRoot;
    }

    private static string ResolveWatchedFilePath(string workspaceRoot, string watchedFilePath)
    {
        string fullRepoRoot = Path.GetFullPath(workspaceRoot);
        string fullPath = Path.IsPathRooted(watchedFilePath)
            ? Path.GetFullPath(watchedFilePath)
            : Path.GetFullPath(Path.Combine(fullRepoRoot, watchedFilePath));

        if (!IsWithinWorkspace(fullRepoRoot, fullPath))
        {
            throw new InvalidOperationException($"Watched file path must stay within the selected workspace: {watchedFilePath}");
        }

        return fullPath;
    }

    private static bool IsWithinWorkspace(string workspaceRoot, string candidatePath)
    {
        string normalizedWorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        string normalizedCandidatePath = Path.GetFullPath(candidatePath);
        if (normalizedCandidatePath.Equals(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = normalizedWorkspaceRoot + Path.DirectorySeparatorChar;
        return normalizedCandidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReviewUrl(string workspaceRoot, StagedEditRecord record)
    {
        string route = string.IsNullOrWhiteSpace(record.SessionId)
            ? $"/review/staged/{Uri.EscapeDataString(record.StagedRecordId)}"
            : $"/review/session/{Uri.EscapeDataString(record.SessionId)}";
        return $"{route}?workspace={Uri.EscapeDataString(Path.GetFullPath(workspaceRoot))}";
    }
}

public sealed record StageForReviewResult(
    StagedEditSummary StagedRecord,
    PreMergeValidationResult Validation,
    string ReviewUrl,
    string Message);
