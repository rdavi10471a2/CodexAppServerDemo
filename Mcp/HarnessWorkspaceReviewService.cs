using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Staged review operations exposed through the local MCP boundary for the currently selected workspace.
/// </summary>
public sealed class HarnessWorkspaceReviewService
{
    private static readonly TimeSpan DefaultGovernedReviewTimeout = TimeSpan.FromMinutes(30);

    private readonly WorkspaceState workspaceState;
    private readonly CodingServicesSettingsProvider settingsProvider;
    private readonly GovernedReviewCoordinatorService governedReviewCoordinator;
    private readonly TimeSpan governedReviewTimeout;

    public HarnessWorkspaceReviewService(
        WorkspaceState workspaceState,
        CodingServicesSettingsProvider settingsProvider,
        GovernedReviewCoordinatorService governedReviewCoordinator,
        TimeSpan? governedReviewTimeout = null)
    {
        this.workspaceState = workspaceState;
        this.settingsProvider = settingsProvider;
        this.governedReviewCoordinator = governedReviewCoordinator;
        this.governedReviewTimeout = governedReviewTimeout ?? DefaultGovernedReviewTimeout;
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
        ThrowIfGovernedReviewIsOwnedByHost(stagedRecordId);
        return CreateReviewService().Accept(GetWorkspaceRoot(), stagedRecordId, forceApproveValidation);
    }

    public StagedReviewPageActionResult Reject(string stagedRecordId)
    {
        ThrowIfGovernedReviewIsOwnedByHost(stagedRecordId);
        return CreateReviewService().Reject(GetWorkspaceRoot(), stagedRecordId);
    }

    public async Task<StageForReviewResult> StageCurrentCandidateForReviewAsync(
        string watchedFilePath,
        string? sessionId = null,
        string? ledgerSummary = null,
        CancellationToken cancellationToken = default)
    {
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
            ? $"Governed review queued in the host UI for {sessionLabel}, but pre-merge validation reported issues. ReviewUrl (diagnostic only): {reviewUrl}"
            : $"Governed review queued in the host UI for {sessionLabel}. ReviewUrl (diagnostic only): {reviewUrl}";
        record = workflowService.RecordDiffLaunch(record.StagedRecordId, launched: true, launchMessage);

        int pendingCount = reviewService.ListPending(workspaceRoot)
            .Count(item => item.SessionId.Equals(record.SessionId, StringComparison.Ordinal));
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(governedReviewTimeout);

        GovernedReviewResolution resolution;
        try
        {
            resolution = await governedReviewCoordinator.QueueAndWaitAsync(
                new GovernedReviewRequest(
                    record.SessionId,
                    record.RelativePath,
                    pendingCount,
                    record.PreMergeValidationIsError,
                    record.PreMergeValidationForceApproved),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Governed review for edit session '{record.SessionId}' timed out after {governedReviewTimeout.TotalMinutes:0} minutes without a host decision.");
        }
        StagedEditRecord refreshedRecord = workflowService.GetStagedRecord(record.StagedRecordId);

        string completionMessage = string.IsNullOrWhiteSpace(resolution.Message)
            ? launchMessage
            : resolution.Message;

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

    private void ThrowIfGovernedReviewIsOwnedByHost(string stagedRecordId)
    {
        string workspaceRoot = GetWorkspaceRoot();
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        WorkflowEditService workflowService = new(settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);

        if (!string.IsNullOrWhiteSpace(record.SessionId) && governedReviewCoordinator.IsSessionPending(record.SessionId))
        {
            throw new InvalidOperationException(
                $"Staged record '{stagedRecordId}' belongs to active governed review session '{record.SessionId}'. Use the host review dialog buttons to accept or reject it instead of calling MCP accept/reject directly.");
        }
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
