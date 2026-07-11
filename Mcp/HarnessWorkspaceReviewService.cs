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
        string sessionId,
        string? ledgerSummary = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Single-file governed staging is retired. Declare the edit-session file set and use stage_edit_session_for_review for every governed review, including queue-of-one single-file sessions.");
    }

    public async Task<StageForReviewResult> StageEditSessionForReviewAsync(
        IReviewElicitor elicitor,
        string sessionId,
        string? ledgerSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (elicitor is null)
        {
            throw new ArgumentNullException(nameof(elicitor));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("stage_edit_session_for_review requires a non-empty sessionId.");
        }

        string workspaceRoot = GetWorkspaceRoot();
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowEditService workflowService = new(settings);
        IStagedReviewPageService reviewService = CreateReviewService();
        EditSessionPlan sessionPlan = workflowService.GetSessionPlan(sessionId)
            ?? throw new InvalidOperationException(
                $"No declared governed file set exists for edit session '{sessionId}'. Declare the session files before staging a multi-file review.");
        if (sessionPlan.DeclaredWatchedFilePaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"Edit session '{sessionId}' does not declare any files. Declare the intended files before staging.");
        }

        List<StagedEditRecord> stagedRecords = [];
        List<PreMergeValidationResult> validations = [];
        foreach (string declaredPath in sessionPlan.DeclaredWatchedFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullWatchedPath = ResolveWatchedFilePath(workspaceRoot, declaredPath);
            EditSessionStatus editSession = workflowService.EnsureEditableSession(fullWatchedPath);
            if (!editSession.EditSessionId.Equals(sessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Active governed edit session mismatch for '{editSession.RelativePath}'. Expected sessionId '{sessionId}', but the working candidate is bound to '{editSession.EditSessionId}'.");
            }

            StagedEditRecord record = workflowService.Stage(fullWatchedPath, ledgerSummary, sessionId);
            record = workflowService.PrepareReviewFileForLaunch(record.StagedRecordId);
            PreMergeValidationResult validation = new PreMergeValidationService().Validate(settings, record);
            record = workflowService.RecordPreMergeValidation(record.StagedRecordId, validation, forceApproved: false);
            string launchMessage = validation.IsError
                ? $"Governed review elicitation raised for edit session '{sessionId}', but pre-merge validation reported issues."
                : $"Governed review elicitation raised for edit session '{sessionId}'.";
            record = workflowService.RecordDiffLaunch(record.StagedRecordId, launched: true, launchMessage);
            stagedRecords.Add(record);
            validations.Add(validation);
        }

        workspaceState.SetCurrentEditSessionId(sessionId);
        string reviewUrl = BuildReviewUrl(workspaceRoot, stagedRecords[0]);
        int pendingCount = reviewService.ListPending(workspaceRoot)
            .Count(item => item.SessionId.Equals(sessionId, StringComparison.Ordinal));
        bool validationIsError = validations.Any(result => result.IsError);
        PreMergeValidationResult summaryValidation = validations.FirstOrDefault(result => result.IsError)
            ?? validations[0];

        ReviewDecision decision = await elicitor.RequestDecisionAsync(
            new ReviewElicitationRequest(
                sessionId,
                string.Join(", ", sessionPlan.DeclaredRelativePaths),
                $"edit session '{sessionId}'",
                pendingCount,
                validationIsError,
                summaryValidation.Status,
                validations.Sum(result => result.DiagnosticCount),
                reviewUrl),
            cancellationToken);

        if (decision == ReviewDecision.Cancelled)
        {
            throw new OperationCanceledException(
                $"Governed review cancelled for edit session '{sessionId}'. Staged records left pending.");
        }

        StagedEditRecord refreshedRecord = workflowService.GetStagedRecord(stagedRecords[0].StagedRecordId);
        string completionMessage = decision == ReviewDecision.Accepted
            ? $"Governed review session completed for edit session '{sessionId}' via the review dialog."
            : $"Governed review declined for edit session '{sessionId}'; staged items left for the operator.";
        return new StageForReviewResult(
            workflowService.CreateSummary(refreshedRecord),
            summaryValidation,
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
