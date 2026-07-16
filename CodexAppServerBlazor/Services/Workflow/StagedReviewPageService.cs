using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Indexing;
using CodexAppServerBlazor.AICodingServices.Logging;
using CodexAppServerBlazor.AICodingServices.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexAppServerBlazor.Services.Workflow;

public interface IStagedReviewPageService
{
    IReadOnlyList<StagedReviewQueueItem> ListPending(string workspaceRoot);

    StagedReviewPageModel Load(string workspaceRoot, string stagedRecordId);

    StagedReviewPageModel LoadNextForSession(string workspaceRoot, string sessionId);

    StagedReviewPageActionResult Accept(string workspaceRoot, string stagedRecordId, bool forceApproveValidation = false);

    StagedReviewPageActionResult Reject(string workspaceRoot, string stagedRecordId);
}

public sealed class StagedReviewPageService : IStagedReviewPageService
{
    private readonly CodingServicesSettingsProvider settingsProvider;
    private readonly ILogger<StagedReviewPageService> logger;

    public StagedReviewPageService(
        CodingServicesSettingsProvider settingsProvider,
        ILogger<StagedReviewPageService>? logger = null)
    {
        this.settingsProvider = settingsProvider;
        this.logger = logger ?? NullLogger<StagedReviewPageService>.Instance;
    }

    public IReadOnlyList<StagedReviewQueueItem> ListPending(string workspaceRoot)
    {
        WorkflowEditService workflowService = CreateWorkflowService(workspaceRoot, out _);
        return workflowService.ListStagedRecords()
            .Where(IsPendingSessionRecord)
            .OrderBy(record => record.CreatedAtUtc, StringComparer.Ordinal)
            .ThenBy(record => record.StagedRecordId, StringComparer.Ordinal)
            .Select(record => new StagedReviewQueueItem(
                record.StagedRecordId,
                record.RelativePath,
                record.SessionId,
                record.IsNewFile,
                record.CreatedAtUtc,
                record.LaunchStatus,
                record.PreMergeValidationStatus))
            .ToArray();
    }

    public StagedReviewPageModel Load(string workspaceRoot, string stagedRecordId)
    {
        WorkflowEditService workflowService = CreateWorkflowService(workspaceRoot, out _);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        return CreateModel(record);
    }

    public StagedReviewPageModel LoadNextForSession(string workspaceRoot, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        WorkflowEditService workflowService = CreateWorkflowService(workspaceRoot, out _);
        StagedEditRecord? record = workflowService.ListStagedRecords(sessionId)
            .Where(IsPendingSessionRecord)
            .OrderBy(record => record.CreatedAtUtc, StringComparer.Ordinal)
            .ThenBy(record => record.StagedRecordId, StringComparer.Ordinal)
            .FirstOrDefault();

        return record is null ? CreateSessionCompleteModel(sessionId) : CreateModel(record);
    }

    public StagedReviewPageActionResult Accept(string workspaceRoot, string stagedRecordId, bool forceApproveValidation = false)
    {
        logger.LogInformation(
            "Staged review accept started. Workspace={WorkspaceRoot} Record={StagedRecordId} ForceApproveValidation={ForceApproveValidation}",
            workspaceRoot,
            stagedRecordId,
            forceApproveValidation);
        WorkflowEditService workflowService = CreateWorkflowService(workspaceRoot, out CodingServicesSettings settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        logger.LogInformation(
            "Staged review accept loaded record. Record={StagedRecordId} Session={SessionId} Path={RelativePath} ValidationStatus={ValidationStatus}",
            record.StagedRecordId,
            record.SessionId,
            record.RelativePath,
            record.PreMergeValidationStatus);
        WorkflowEditService.EnsureRecordNotDecided(record);
        if (forceApproveValidation && record.PreMergeValidationIsError && !record.PreMergeValidationForceApproved)
        {
            logger.LogInformation(
                "Staged review accept applying validation override. Record={StagedRecordId}",
                record.StagedRecordId);
            record = workflowService.ApprovePreMergeValidationFailure(stagedRecordId);
        }

        StagedReviewDecisionOptions decisionOptions = CreateDecisionOptions(
            record,
            GetSessionRecords(workflowService, record),
            "accepted");
        logger.LogInformation(
            "Staged review accept decision options built. Record={StagedRecordId} DeferIndexRefresh={DeferIndexRefresh} RefreshFiles={RefreshFileCount} TerminalValidationRecords={TerminalValidationRecordCount}",
            record.StagedRecordId,
            decisionOptions.DeferIndexRefresh,
            decisionOptions.RefreshPlan?.ChangedFilePaths.Count ?? 0,
            decisionOptions.TerminalValidationRecords.Count);
        EnsureTerminalValidationPassesBeforeCopy(settings, record, decisionOptions);
        logger.LogInformation(
            "Staged review accept terminal validation passed. Record={StagedRecordId}",
            record.StagedRecordId);

        if (!File.Exists(record.StagedFilePath))
        {
            throw new FileNotFoundException("Staged candidate file was not found.", record.StagedFilePath);
        }

        string? watchedDirectory = Path.GetDirectoryName(record.WatchedFilePath);
        if (!string.IsNullOrWhiteSpace(watchedDirectory))
        {
            Directory.CreateDirectory(watchedDirectory);
        }

        logger.LogInformation(
            "Staged review accept copying staged file into watched source. Record={StagedRecordId} StagedPath={StagedPath} WatchedPath={WatchedPath}",
            record.StagedRecordId,
            record.StagedFilePath,
            record.WatchedFilePath);
        File.Copy(record.StagedFilePath, record.WatchedFilePath, overwrite: true);
        logger.LogInformation(
            "Staged review accept copy completed. Record={StagedRecordId}",
            record.StagedRecordId);
        ReviewDecisionWithIndexRefreshResult result = RecordDecision(
            settings,
            workflowService,
            record,
            "accepted",
            record.StagedHash,
            decisionOptions);
        logger.LogInformation(
            "Staged review accept record decision completed. Record={StagedRecordId} NextStep={NextStep} RefreshStatus={RefreshStatus} RefreshMode={RefreshMode}",
            record.StagedRecordId,
            result.NextStep,
            result.IndexRefresh?.Status ?? "<none>",
            result.IndexRefresh?.RefreshMode ?? "<none>");
        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        logger.LogInformation(
            "Staged review accept finished. Record={StagedRecordId} Decision={Decision} Classification={Classification}",
            decided.StagedRecordId,
            decided.Decision,
            decided.Classification);
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            decided.PreMergeValidationForceApproved
                ? $"Accepted proposed candidate into current source with an explicit pre-merge validation override. {result.NextStep}"
                : $"Accepted proposed candidate into current source. {result.NextStep}");
    }

    public StagedReviewPageActionResult Reject(string workspaceRoot, string stagedRecordId)
    {
        logger.LogInformation(
            "Staged review reject started. Workspace={WorkspaceRoot} Record={StagedRecordId}",
            workspaceRoot,
            stagedRecordId);
        WorkflowEditService workflowService = CreateWorkflowService(workspaceRoot, out CodingServicesSettings settings);
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(record);
        ReviewDecisionWithIndexRefreshResult result = RecordDecision(
            settings,
            workflowService,
            record,
            "rejected",
            expectedStagedHash: null,
            decisionOptions: null);
        StagedEditRecord decided = workflowService.GetStagedRecord(record.StagedRecordId);
        logger.LogInformation(
            "Staged review reject finished. Record={StagedRecordId} NextStep={NextStep}",
            decided.StagedRecordId,
            result.NextStep);
        return new StagedReviewPageActionResult(
            CreateModel(decided),
            $"Rejected proposed candidate. Current source was left unchanged. {result.NextStep}");
    }

    private WorkflowEditService CreateWorkflowService(string workspaceRoot, out CodingServicesSettings settings)
    {
        settings = settingsProvider.GetSettings(workspaceRoot);
        return new WorkflowEditService(settings);
    }

    private static ReviewDecisionWithIndexRefreshResult RecordDecision(
        CodingServicesSettings settings,
        WorkflowEditService workflowService,
        StagedEditRecord record,
        string decision,
        string? expectedStagedHash,
        StagedReviewDecisionOptions? decisionOptions)
    {
        StagedReviewDecisionOptions resolvedDecisionOptions = decisionOptions ?? CreateDecisionOptions(
            record,
            GetSessionRecords(workflowService, record),
            decision);
        return new StagedDecisionWorkflow().Record(
            settings,
            NoOpMonitorLogger.Instance,
            workflowService,
            record.StagedRecordId,
            decision,
            expectedStagedHash,
            nameof(CodexAppServerBlazor),
            deferIndexRefresh: resolvedDecisionOptions.DeferIndexRefresh,
            refreshPlan: resolvedDecisionOptions.RefreshPlan,
            terminalValidationRecords: resolvedDecisionOptions.TerminalValidationRecords);
    }

    private static void EnsureTerminalValidationPassesBeforeCopy(
        CodingServicesSettings settings,
        StagedEditRecord record,
        StagedReviewDecisionOptions decisionOptions)
    {
        if (decisionOptions.DeferIndexRefresh
            || decisionOptions.RefreshPlan is null
            || decisionOptions.RefreshPlan.ChangedFilePaths.Count == 0
            || decisionOptions.TerminalValidationRecords.Count == 0)
        {
            return;
        }

        PreMergeValidationResult validation = new PreMergeValidationService().Validate(
            settings,
            record,
            decisionOptions.TerminalValidationRecords);
        if (validation.IsError && !record.PreMergeValidationForceApproved)
        {
            throw new InvalidOperationException(
                "Terminal planned pre-merge validation failed before copying the staged candidate into source: "
                + validation.Message);
        }
    }

    private static IReadOnlyList<StagedEditRecord> GetSessionRecords(
        WorkflowEditService workflowService,
        StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.SessionId)
            ? []
            : workflowService.ListStagedRecords(record.SessionId);
    }

    private static StagedReviewDecisionOptions CreateDecisionOptions(
        StagedEditRecord currentRecord,
        IReadOnlyList<StagedEditRecord> sessionRecords,
        string requestedDecision)
    {
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId) || sessionRecords.Count == 0)
        {
            return new StagedReviewDecisionOptions(false, null, []);
        }

        bool hasOtherPendingRecords = sessionRecords.Any(record =>
            !record.StagedRecordId.Equals(currentRecord.StagedRecordId, StringComparison.Ordinal)
            && IsPendingSessionRecord(record));
        bool acceptingCurrentRecord = requestedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase);
        StagedEditRecord[] acceptedRecords = sessionRecords
            .Append(currentRecord)
            .Where(record => record.Classification is "accepted" or "accepted-normalized"
                || (acceptingCurrentRecord && record.StagedRecordId.Equals(currentRecord.StagedRecordId, StringComparison.Ordinal)))
            .GroupBy(record => Path.GetFullPath(record.WatchedFilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.CreatedAtUtc, StringComparer.Ordinal).First())
            .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (acceptedRecords.Length == 0)
        {
            return new StagedReviewDecisionOptions(hasOtherPendingRecords, null, []);
        }

        PostAcceptIndexRefreshPlan refreshPlan = new()
        {
            ChangedFilePaths = acceptedRecords.Select(record => record.WatchedFilePath).ToArray(),
            OwningProjectPaths = []
        };
        return new StagedReviewDecisionOptions(
            hasOtherPendingRecords,
            refreshPlan,
            hasOtherPendingRecords ? [] : acceptedRecords);
    }

    private static bool IsPendingSessionRecord(StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.Decision)
            && string.IsNullOrWhiteSpace(record.SupersededByStagedRecordId)
            && !record.Status.Equals("superseded", StringComparison.OrdinalIgnoreCase)
            && !record.Classification.Equals("superseded", StringComparison.OrdinalIgnoreCase);
    }

    private static StagedReviewPageModel CreateModel(StagedEditRecord record)
    {
        string currentPath = ResolveCurrentPath(record);
        string proposedPath = ResolveProposedPath(record);
        string currentText = File.Exists(currentPath) ? File.ReadAllText(currentPath) : string.Empty;
        string proposedText = File.Exists(proposedPath) ? File.ReadAllText(proposedPath) : string.Empty;
        bool isDecided = !string.IsNullOrWhiteSpace(record.Decision);
        string decisionStatus = isDecided
            ? $"{record.Decision} ({record.Classification})"
            : "Pending review";

        return new StagedReviewPageModel(
            record.StagedRecordId,
            record.RelativePath,
            currentPath,
            proposedPath,
            record.WatchedFilePath,
            currentText,
            proposedText,
            decisionStatus,
            isDecided,
            record.IsNewFile,
            record.StagedHash,
            record.SessionId,
            record.CreatedAtUtc,
            record.LaunchStatus,
            record.PreMergeValidationStatus,
            record.PreMergeValidationIsError,
            record.PreMergeValidationForceApproved,
            IsSessionComplete: false);
    }

    private static StagedReviewPageModel CreateSessionCompleteModel(string sessionId)
    {
        return new StagedReviewPageModel(
            string.Empty,
            $"Session {sessionId}",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "Session complete",
            IsDecided: true,
            IsNewFile: false,
            string.Empty,
            sessionId,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            IsSessionComplete: true);
    }

    private static string ResolveCurrentPath(StagedEditRecord record)
    {
        return string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath)
            ? record.WatchedFilePath
            : record.ReviewBaselineFilePath;
    }

    private static string ResolveProposedPath(StagedEditRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.WorkingFilePath)
            ? record.WorkingFilePath
            : record.StagedFilePath;
    }

    private sealed record StagedReviewDecisionOptions(
        bool DeferIndexRefresh,
        PostAcceptIndexRefreshPlan? RefreshPlan,
        IReadOnlyList<StagedEditRecord> TerminalValidationRecords);

    private sealed class NoOpMonitorLogger : IMonitorLogger
    {
        public static readonly NoOpMonitorLogger Instance = new();

        public void Write(
            MonitorLogLevel level,
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }
    }
}

public sealed record StagedReviewQueueItem(
    string StagedRecordId,
    string RelativePath,
    string SessionId,
    bool IsNewFile,
    string CreatedAtUtc,
    string LaunchStatus,
    string PreMergeValidationStatus);

public sealed record StagedReviewPageModel(
    string StagedRecordId,
    string RelativePath,
    string CurrentPath,
    string ProposedPath,
    string WatchedFilePath,
    string CurrentText,
    string ProposedText,
    string DecisionStatus,
    bool IsDecided,
    bool IsNewFile,
    string StagedHash,
    string SessionId,
    string CreatedAtUtc,
    string LaunchStatus,
    string PreMergeValidationStatus,
    bool PreMergeValidationIsError,
    bool PreMergeValidationForceApproved,
    bool IsSessionComplete);

public sealed record StagedReviewPageActionResult(
    StagedReviewPageModel Model,
    string Message);
