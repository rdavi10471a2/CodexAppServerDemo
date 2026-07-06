using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICodingServices.McpServer;

public sealed partial class AICodingServicesTools
{
    [McpServerTool]
    [Description("Return edit workflow status for one watched source file.")]
    public EditSessionStatus GetEditStatus(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        EditSessionStatus status = workflowService.GetStatus(ResolveWatchedPath(sourceFilePath));
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-edit-status", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        }

        return status;
    }

    [McpServerTool]
    [Description("Write a full-file candidate into the monitor-owned Working mirror. Does not create a staged record.")]
    public EditSessionStatus SubmitFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Complete replacement file content.")] string content,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        EditSessionStatus status = workflowService.SubmitFile(fullPath, content, manifestJson, !deferOverlayValidation);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "submit-file", fullPath, manifestJson);
        }

        return status;
    }

    [McpServerTool]
    [Description("Replace exact oldText in the monitor-owned Working mirror candidate.")]
    public ReplaceTextResult ReplaceTextInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact old text to replace using ordinal matching.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("Required number of matches in the current edit base. Leave -1 for unique replacement when occurrenceIndex is unset, or no total-match assertion when occurrenceIndex is set.")] int expectedMatches = -1,
        [Description("Optional 0-based occurrence index. Leave -1 for unique/global replacement; set 0 or greater to replace one occurrence without requiring unique oldText.")] int occurrenceIndex = -1,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of oldText.")] string? expectedOldTextHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        if (!string.IsNullOrWhiteSpace(expectedOldTextHash)
            && !ComputeHash(oldText).Equals(expectedOldTextHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("oldText hash did not match expectedOldTextHash.");
        }

        int? expectedMatchCount = expectedMatches >= 0
            ? expectedMatches
            : occurrenceIndex >= 0 ? null : 1;

        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        ReplaceTextResult result = workflowService.ReplaceText(
            fullPath,
            oldText,
            newText,
            expectedMatchCount,
            expectedFileHash,
            occurrenceIndex >= 0 ? occurrenceIndex : null,
            manifestJson,
            !deferOverlayValidation);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "replace-text-in-file", result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Find exact text in the current Working candidate and return 1-based line/column bounds.")]
    public TextSpanResult FindTextSpan(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact text to find using ordinal matching.")] string findText,
        [Description("0-based occurrence index when text appears multiple times.")] int occurrenceIndex = 0,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null)
    {
        runtimeState.Touch();
        _ = sessionId;
        string fullPath = ResolveWatchedPath(path);
        EnsureSession(fullPath);
        return workflowService.FindTextSpan(fullPath, findText, occurrenceIndex, expectedFileHash);
    }

    [McpServerTool]
    [Description("Replace an exact 1-based line/column span in the monitor-owned Working mirror candidate.")]
    public EditSessionStatus ReplaceSpanInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("1-based start line.")] int startLine,
        [Description("1-based start column.")] int startColumn,
        [Description("1-based exclusive end line.")] int endLine,
        [Description("1-based exclusive end column.")] int endColumn,
        [Description("Replacement text.")] string newText,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of the extracted old span text.")] string? expectedOldTextHash = null,
        [Description("Optional exact old span text.")] string? expectedOldText = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath);
        EnsureSession(fullPath);
        bool deferOverlayValidation = ShouldDeferPlannedOverlayValidation(sessionId, fullPath);
        EditSessionStatus status = workflowService.ReplaceSpan(
            fullPath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            newText,
            expectedFileHash,
            expectedOldTextHash,
            expectedOldText,
            manifestJson,
            !deferOverlayValidation);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "replace-span-in-file", status.WatchedFilePath, null);
        }

        return status;
    }

    [McpServerTool]
    [Description("Stage the current Working mirror candidate for review. This creates one immutable staged record from the completed candidate.")]
    public AICodingServicesStageCandidateResult StageCandidateForReview(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Optional compact ledger summary.")] string? ledgerSummary = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsurePlannedMutationAllowed(sessionId, fullPath);
        StagedEditRecord record = workflowService.Stage(fullPath, ledgerSummary, sessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "stage-candidate-for-review", record.StagedRecordId, JsonSerializer.Serialize(record, JsonOptions));
        }

        StagedEditSummary summary = workflowService.CreateSummary(record);
        return new AICodingServicesStageCandidateResult(
            summary.StagedRecordId,
            summary.StagedHash,
            summary.Status,
            summary.Classification,
            summary.RecordPath,
            summary,
            verbose ? record : null,
            "Candidate staged. Use get_staged_record for full details or launch_staged_diff for review.");
    }

    [McpServerTool]
    [Description("Classify a completed WinMerge review for a staged edit. Accepted decisions require the expected staged hash.")]
    public ReviewDecisionWithIndexRefreshResult RecordDiffDecision(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Operator-reported outcome: accepted or rejected.")] string decision,
        [Description("Expected staged hash for accepted decisions.")] string? expectedStagedHash = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        PlannedSessionDecisionOptions decisionOptions = BuildPlannedSessionDecisionOptions(stagedRecordId, decision);
        return new StagedDecisionWorkflow().Record(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            decision,
            expectedStagedHash,
            "AICodingServices.McpServer",
            decisionOptions.DeferIndexRefresh,
            decisionOptions.RefreshPlan,
            verbose,
            decisionOptions.TerminalValidationRecords);
    }

    [McpServerTool]
    [Description("Run pre-merge validation, then launch the configured review surface for a staged edit record and return review paths.")]
    public AICodingServicesStagedDiffLaunchResult LaunchStagedDiff(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Explicit diff tool executable path.")] string? diffToolPath = null,
        [Description("Force launch after an explicit human validation override.")] bool forceValidation = false,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord stagedRecord = workflowService.GetStagedRecord(stagedRecordId);
        bool deferBuildValidationUntilAccept = ShouldDeferBuildValidationUntilAccept(stagedRecord);
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AICodingServices.McpServer",
            diffToolPath,
            forceValidation,
            deferBuildValidationUntilAccept,
            verbose);
        return new AICodingServicesStagedDiffLaunchResult(
            result.StagedRecordSummary,
            result.StagedRecord,
            result.PreMergeValidation,
            result.CommandReductions,
            result.DiffLaunch,
            result.NextStep);
    }

    [McpServerTool]
    [Description("Run pre-merge validation, then launch the CodexUI browser staged review page for a staged edit record.")]
    public AICodingServicesStagedDiffLaunchResult LaunchStagedBrowserReview(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("CodexUI base URL used to build /review/staged/{stagedRecordId}. Defaults to http://localhost:5000.")] string browserReviewBaseUrl = "http://localhost:5000",
        [Description("Explicit browser executable path. When omitted, the system default browser handles the URL.")] string? browserPath = null,
        [Description("Force launch after an explicit human validation override.")] bool forceValidation = false,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord stagedRecord = workflowService.GetStagedRecord(stagedRecordId);
        bool deferBuildValidationUntilAccept = ShouldDeferBuildValidationUntilAccept(stagedRecord);
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AICodingServices.McpServer",
            diffToolPath: null,
            forceValidation: forceValidation,
            deferBuildValidationUntilAccept: deferBuildValidationUntilAccept,
            verbose: verbose,
            launchSurface: StagedReviewLaunchSurface.Browser,
            browserReviewBaseUrl: browserReviewBaseUrl,
            browserPath: browserPath);
        return new AICodingServicesStagedDiffLaunchResult(
            result.StagedRecordSummary,
            result.StagedRecord,
            result.PreMergeValidation,
            result.CommandReductions,
            result.DiffLaunch,
            result.NextStep);
    }

    [McpServerTool]
    [Description("Return the full persisted staged edit record by id. Use after compact stage/launch/decision replies when debug detail is needed.")]
    public StagedEditRecord GetStagedRecord(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId)
    {
        runtimeState.Touch();
        return workflowService.GetStagedRecord(stagedRecordId);
    }

    [McpServerTool]
    [Description("Create a proposed compare snapshot for a monitor Working file and return review paths.")]
    public CompareSnapshotResult CompareFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional compact ledger summary to append to the monitor-owned ledger.")] string? ledgerSummary = null,
        [Description("Refresh from source first if the Working copy is missing.")] bool refreshIfMissing = true,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string path = ResolveWatchedPath(sourceFilePath);
        if (refreshIfMissing && !workflowService.GetStatus(path).WorkingFileExists)
        {
            workflowService.Refresh(path);
        }

        CompareSnapshotResult result = workflowService.Compare(path, ledgerSummary);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "compare-file", result.WorkingFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("List monitor run/history entries recorded under monitor-owned workflow history.")]
    public IReadOnlyList<Dictionary<string, object?>> ListMonitorRuns(
        [Description("Maximum entries to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string path = Path.Combine(workflowPaths.HistoryRoot, "_runs.json");
        if (!File.Exists(path))
        {
            return [];
        }

        IReadOnlyList<Dictionary<string, object?>> entries = JsonSerializer.Deserialize<IReadOnlyList<Dictionary<string, object?>>>(File.ReadAllText(path), JsonOptions) ?? [];
        return entries.TakeLast(maxEntries).ToArray();
    }

    [McpServerTool]
    [Description("Return recorded entries for one monitor run id.")]
    public IReadOnlyList<Dictionary<string, object?>> GetMonitorRun(
        [Description("Run id from list_monitor_runs.")] string runId)
    {
        runtimeState.Touch();
        return ListMonitorRuns(500)
            .Where(entry => entry.TryGetValue("runId", out object? value) && string.Equals(value?.ToString(), runId, StringComparison.Ordinal))
            .ToArray();
    }

    [McpServerTool]
    [Description("List monitor-owned per-file ledgers.")]
    public IReadOnlyList<AICodingServicesLedgerInfo> ListLedgers(
        [Description("Maximum ledgers to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string root = Path.Combine(workflowPaths.HistoryRoot, "Ledgers");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.md")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(maxEntries)
                .Select(info => new AICodingServicesLedgerInfo(info.FullName, info.Length, info.LastWriteTimeUtc))
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Read one monitor-owned per-file ledger by source file or ledger path.")]
    public AICodingServicesLedgerReadResult GetLedger(
        [Description("Optional source file path, absolute or relative to the watched solution folder.")] string? sourceFilePath = null,
        [Description("Optional absolute ledger path under the ledger root.")] string? ledgerPath = null)
    {
        runtimeState.Touch();
        string root = Path.GetFullPath(Path.Combine(workflowPaths.HistoryRoot, "Ledgers"));
        string path = !string.IsNullOrWhiteSpace(ledgerPath)
            ? Path.GetFullPath(ledgerPath)
            : Path.Combine(root, $"{Sanitize(workflowPaths.GetRelativeWatchedPath(ResolveWatchedPath(sourceFilePath ?? throw new InvalidOperationException("sourceFilePath or ledgerPath is required."))).Replace(Path.DirectorySeparatorChar, '_'))}.md");
        string relativeLedgerPath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativeLedgerPath)
            || relativeLedgerPath.Equals("..", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ledger path must be under monitor-owned ledger storage.");
        }

        return new AICodingServicesLedgerReadResult(path, File.Exists(path), File.Exists(path) ? File.ReadAllText(path) : string.Empty);
    }

    [McpServerTool]
    [Description("Archive/prune monitor-owned history. AICodingServices keeps history by default; this compatibility tool reports the current retention posture without deleting files.")]
    public AICodingServicesCompatibilityResult PruneMonitorHistory(
        [Description("Retention window in days.")] int retentionDays = 7)
    {
        runtimeState.Touch();
        return new AICodingServicesCompatibilityResult(
            "not-pruned",
            "AICodingServices currently keeps workflow history until an explicit UI/operator cleanup flow is implemented.",
            new Dictionary<string, string?> { ["retentionDays"] = retentionDays.ToString() });
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the AICodingServices MCP Server tool surface.")]
    public string GetToolManifest()
    {
        runtimeState.Touch();
        return ComposeToolManifest();
    }

    [McpServerTool]
    [Description("Return the normal staging guide for AICodingServices watched-project edits.")]
    public string GetStagingGuide()
    {
        runtimeState.Touch();
        return ComposeStagingGuide();
    }

    [McpServerTool]
    [Description("Return the smoke-test coverage todo/catalog for AICodingServices.")]
    public string GetSmokeTestCatalog()
    {
        runtimeState.Touch();
        string path = Path.Combine(settings.RepositoryRoot, "docs", "findings", "SmokeCoverageTodo.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "AICodingServices smoke coverage catalog is missing.";
    }

    [McpServerTool]
    [Description("List watched project folders. AICodingServices currently has one configured watched project folder.")]
    public IReadOnlyList<AICodingServicesWatchedProjectInfo> ListWatchedProjects()
    {
        runtimeState.Touch();
        return
        [
            new AICodingServicesWatchedProjectInfo(
                Path.GetFileName(settings.WatchedProjectFolder),
                settings.WatchedProjectFolder,
                File.Exists(settings.WatchedSolutionPath) ? [settings.WatchedSolutionPath] : [])
        ];
    }

    [McpServerTool]
    [Description("Request graceful shutdown of this AICodingServices MCP server process.")]
    public AICodingServicesServerShutdownResult ShutdownServer(
        [Description("Optional operator/client reason for the shutdown request.")] string? reason = null)
    {
        runtimeState.RequestShutdown(reason);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            applicationLifetime.StopApplication();
        });
        return new AICodingServicesServerShutdownResult(Environment.ProcessId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(reason) ? "shutdown_server requested" : reason);
    }

    private string ComposeToolManifest()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AICodingServices MCP Tool Manifest");
        builder.AppendLine();
        builder.AppendLine("This manifest is generated from the currently loaded AICodingServices MCP tool methods.");
        builder.AppendLine();
        foreach (MethodInfo method in typeof(AICodingServicesTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => ToToolName(method.Name), StringComparer.Ordinal))
        {
            string toolName = ToToolName(method.Name);
            string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description.";
            builder.AppendLine($"## `{toolName}`");
            builder.AppendLine();
            builder.AppendLine(description);
            builder.AppendLine();
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                builder.AppendLine("Parameters:");
                foreach (ParameterInfo parameter in parameters)
                {
                    string parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                    string nullable = IsNullableParameter(parameter) ? "optional" : "required";
                    builder.AppendLine($"- `{parameter.Name}` ({parameter.ParameterType.Name}, {nullable}): {parameterDescription}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Safety Notes");
        builder.AppendLine();
        builder.AppendLine("- Watched source is not edited directly by agents.");
        builder.AppendLine("- Existing files enter through `refresh_file`; future files enter through `new_file`.");
        builder.AppendLine("- MCP edit sessions start with `start_monitor_session` and a non-empty `filesPlanned` list.");
        builder.AppendLine("- Candidate edits happen in monitor-owned Working files.");
        builder.AppendLine("- Review uses `stage_candidate_for_review`, `launch_staged_diff`, WinMerge review/save, and `record_diff_decision`.");
        builder.AppendLine("- Planned sessions require all planned files to be staged before review launch.");
        builder.AppendLine("- Planned sessions defer the expensive build/index pass until all planned files are accepted/rejected.");
        builder.AppendLine("- Accepted decisions trigger index refresh metadata after the planned session reaches terminal decisions; refresh before editing the same watched file again.");
        return builder.ToString();
    }

    private string ComposeStagingGuide()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AICodingServices Staging Guide");
        builder.AppendLine();
        builder.AppendLine("Use this sequence for watched-project edits through MCP.");
        builder.AppendLine();
        builder.AppendLine("1. Check `get_self_check`, `get_workflow_status`, and `get_monitor_status` when starting a session.");
        builder.AppendLine("2. Call `start_monitor_session(filesPlanned: [...])` before editing. Include every watched file the session intends to mutate, even for one-file edits, and include `owningProjectPath` when the index cannot prove a single owner.");
        builder.AppendLine("3. Pass that same `sessionId` to `refresh_file`, `new_file`, every mutation tool, and `stage_candidate_for_review`.");
        builder.AppendLine("4. For existing files, call `refresh_file`. For future watched files, call `new_file`.");
        builder.AppendLine("5. Edit only the monitor-owned Working candidate with `submit_file`, text/span tools, or Roslyn typed edit tools.");
        builder.AppendLine("6. Stage every planned file with `stage_candidate_for_review(path, sessionId)`.");
        builder.AppendLine("7. Launch review with `launch_staged_diff` for every planned staged record before recording decisions; planned sessions require the full staged file set before WinMerge opens.");
        builder.AppendLine("8. The operator reviews/saves every planned file in WinMerge. WinMerge is the watched-source mutation surface.");
        builder.AppendLine("9. Record each result with `record_diff_decision`.");
        builder.AppendLine("10. After the last planned file reaches a terminal decision, inspect `indexRefresh` and call `refresh_file` before editing any accepted watched file again.");
        builder.AppendLine();
        builder.AppendLine("Failure paths:");
        builder.AppendLine();
        builder.AppendLine("- `blocked`, `dirty-unexpected`, `superseded`, missing Working files, and stale hashes require recovery before follow-up edits.");
        builder.AppendLine("- Planned-session launch checks staged overlay readiness; the full build/index validation runs at the terminal planned accept, not once per staged file.");
        builder.AppendLine("- Do not manually copy candidates into watched source outside WinMerge/decision classification.");
        return builder.ToString();
    }
}
