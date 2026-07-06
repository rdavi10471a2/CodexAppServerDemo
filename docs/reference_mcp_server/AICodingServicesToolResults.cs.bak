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

public sealed record AICodingServicesMcpStatus(
    string RepositoryRoot,
    string RuntimeRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string DatabasePath,
    bool DatabaseExists,
    int ProjectCount,
    int DocumentCount,
    int SymbolCount,
    int ReferenceCount,
    int CallSiteCount,
    int RelationshipCount,
    int StaleFileCount,
    int DiagnosticCount);

public sealed record AICodingServicesWorkflowStatus(
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string RuntimeRoot,
    string WorkingRoot,
    string? ResolvedDiffToolPath,
    IReadOnlyList<string> WinMergeCandidatePaths);

public sealed record AICodingServicesToolErrorResult(
    bool IsError,
    string Message,
    string Expected,
    string? Received);

public sealed record AICodingServicesIndexedReferenceResult(
    string TargetStableKey,
    string FilePath,
    int Line,
    int Column,
    string ReferenceKind,
    string Snippet,
    string TargetName,
    string TargetKind,
    string CallerStableKey,
    string CallerName,
    string CallerKind);

public sealed record AICodingServicesSolutionIndexTree(
    IReadOnlyList<IndexedProjectRow> Projects,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<AICodingServicesNamespaceTree> Namespaces);

public sealed record AICodingServicesNamespaceTree(
    string Namespace,
    IReadOnlyList<string> Files,
    int SymbolCount);

public sealed record AICodingServicesStageCandidateResult(
    string StagedRecordId,
    string StagedHash,
    string Status,
    string Classification,
    string StagedRecordPath,
    StagedEditSummary StagedRecordSummary,
    StagedEditRecord? StagedRecord,
    string NextStep);

public sealed record AICodingServicesStagedDiffLaunchResult(
    StagedEditSummary StagedRecordSummary,
    StagedEditRecord? StagedRecord,
    PreMergeValidationResult PreMergeValidation,
    GovernedCommandReductionResult[] CommandReductions,
    DiffLaunchResult DiffLaunch,
    string NextStep);

public sealed record AICodingServicesSelfCheckResult(
    string RepositoryRoot,
    string RuntimeRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string WorkingRoot,
    string HistoryRoot,
    string StagedRoot,
    bool WatchedSolutionExists,
    bool WatchedProjectFolderExists,
    string? ResolvedDiffToolPath,
    string SafetySummary,
    string OverallStatus,
    IReadOnlyList<AICodingServicesGuardrailCheck> Guardrails);

public sealed record AICodingServicesGuardrailCheck(
    string Name,
    string Status,
    string Message,
    string? Path);

public sealed record AICodingServicesRefreshIndexFileResult(
    SolutionIndexSummary Summary,
    MonitorStatusResult Status,
    long ElapsedMilliseconds,
    IndexedFileDetailResult Detail,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<IndexedSymbolRow> Symbols);

public sealed record AICodingServicesRefreshIndexResult(
    SolutionIndexSummary Summary,
    MonitorStatusResult Status,
    long ElapsedMilliseconds);

public sealed record AICodingServicesRefreshFileAndIndexResult(
    EditSessionStatus Refresh,
    AICodingServicesRefreshIndexFileResult Index);

public sealed record AICodingServicesSessionState(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AICodingServicesSessionEvent> Events)
{
    public IReadOnlyList<AICodingServicesSessionFileAccess> Files { get; init; } = [];

    public AICodingServicesSessionEditPlan? EditPlan { get; init; }
}

public sealed record AICodingServicesSessionEditPlan(
    DateTimeOffset DeclaredAtUtc,
    IReadOnlyList<AICodingServicesSessionPlannedFile> FilesPlanned);

public sealed record AICodingServicesSessionPlannedFile(
    string SourceFilePath,
    string RelativePath,
    string OwningProjectPath,
    string FileName,
    string ProjectName,
    string Role,
    string Reason);

public sealed record AICodingServicesSessionPlannedFileInput(
    string SourceFilePath,
    string? OwningProjectPath = null,
    string? Role = null,
    string? Reason = null);

public sealed record PlannedSessionDecisionOptions(
    bool DeferIndexRefresh,
    PostAcceptIndexRefreshPlan? RefreshPlan,
    IReadOnlyList<StagedEditRecord> TerminalValidationRecords);

public sealed record AICodingServicesSessionSummary(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int EventCount);

public sealed record AICodingServicesSessionEvent(
    DateTimeOffset TimestampUtc,
    string EventType,
    string Summary,
    string? PayloadJson);

public sealed record AICodingServicesSessionFileAccess(
    string SessionId,
    string SourceFilePath,
    string RelativePath,
    string AccessKind,
    AICodingServicesFileHashInfo Hash,
    int FetchCount,
    DateTimeOffset FirstAccessedAtUtc,
    DateTimeOffset LastAccessedAtUtc);

public sealed record AICodingServicesFileHashInfo(
    string Sha256,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record AICodingServicesFileReadResult(
    string SourceFilePath,
    string RelativePath,
    AICodingServicesFileHashInfo Hash,
    AICodingServicesSessionFileAccess? SessionAccess,
    string Content);

public sealed record AICodingServicesFileHashCheckResult(
    string SourceFilePath,
    bool KnownInSession,
    bool ChangedSinceFetch,
    AICodingServicesFileHashInfo Current,
    AICodingServicesFileHashInfo? Previous,
    AICodingServicesSessionFileAccess? PreviousAccess);

public sealed record AICodingServicesFileMatch(
    string Name,
    string Path,
    string RelativePath);

public sealed record AICodingServicesCompatibilityResult(
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Arguments);

public sealed record AICodingServicesLedgerInfo(
    string Path,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record AICodingServicesLedgerReadResult(
    string Path,
    bool Exists,
    string Content);

public sealed record AICodingServicesWatchedProjectInfo(
    string Name,
    string Path,
    IReadOnlyList<string> SolutionFiles);

public sealed record AICodingServicesServerShutdownResult(
    int ProcessId,
    DateTimeOffset RequestedAtUtc,
    string Reason);
