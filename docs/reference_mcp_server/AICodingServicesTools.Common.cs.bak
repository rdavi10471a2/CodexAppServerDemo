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
    private AICodingServicesGuardrailCheck[] BuildSelfCheckGuardrails()
    {
        string repositoryRoot = Path.GetFullPath(settings.RepositoryRoot);
        string runtimeRoot = Path.GetFullPath(settings.RuntimeRoot);
        string watchedProjectFolder = Path.GetFullPath(settings.WatchedProjectFolder);
        string workingRoot = Path.GetFullPath(workflowPaths.WorkingRoot);
        string historyRoot = Path.GetFullPath(workflowPaths.HistoryRoot);
        string stagedRoot = Path.GetFullPath(workflowPaths.StagedRoot);
        List<AICodingServicesGuardrailCheck> checks =
        [
            CheckPathExists("repository-root-exists", repositoryRoot, Directory.Exists(repositoryRoot), "Repository root exists.", "Repository root is missing."),
            CheckPathExists("watched-solution-exists", settings.WatchedSolutionPath, File.Exists(settings.WatchedSolutionPath), "Watched solution/project exists.", "Watched solution/project is missing."),
            CheckPathExists("watched-project-folder-exists", watchedProjectFolder, Directory.Exists(watchedProjectFolder), "Watched project folder exists.", "Watched project folder is missing."),
            CheckPathExists("runtime-root-exists", runtimeRoot, Directory.Exists(runtimeRoot), "Runtime root exists.", "Runtime root is missing."),
            CheckPathUnderRoot("working-under-runtime", workingRoot, runtimeRoot, "Working root is under runtime root.", "Working root is outside runtime root."),
            CheckPathUnderRoot("history-under-runtime", historyRoot, runtimeRoot, "History root is under runtime root.", "History root is outside runtime root."),
            CheckPathUnderRoot("staged-under-runtime", stagedRoot, runtimeRoot, "Staged root is under runtime root.", "Staged root is outside runtime root."),
            CheckPathOutsideRoot("runtime-outside-watched-source", runtimeRoot, watchedProjectFolder, "Runtime state is outside watched source.", "Runtime state is inside watched source."),
        ];

        string? diffTool = settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists);
        checks.Add(diffTool is null
            ? new AICodingServicesGuardrailCheck("diff-tool-available", "warning", "No configured WinMerge candidate exists on disk.", string.Join(";", settings.WinMergeCandidatePaths))
            : new AICodingServicesGuardrailCheck("diff-tool-available", "passed", "Configured WinMerge candidate exists.", diffTool));

        return checks.ToArray();
    }

    private static AICodingServicesGuardrailCheck CheckPathExists(string name, string path, bool passed, string passedMessage, string failedMessage)
    {
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AICodingServicesGuardrailCheck CheckPathUnderRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = IsPathUnderRoot(path, root);
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AICodingServicesGuardrailCheck CheckPathOutsideRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = !IsPathUnderRoot(path, root);
        return new AICodingServicesGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNullableParameter(ParameterInfo parameter)
    {
        return parameter.HasDefaultValue
            || Nullable.GetUnderlyingType(parameter.ParameterType) is not null
            || !parameter.ParameterType.IsValueType;
    }

    private static string ToToolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private string SessionRoot => Path.Combine(MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings), "workflow", "sessions");

    private string ResolveWatchedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(settings.WatchedProjectFolder, path));
        workflowPaths.GetRelativeWatchedPath(fullPath);
        return fullPath;
    }

    private EditSessionStatus EnsureSession(string watchedFilePath)
    {
        return workflowService.EnsureEditableSession(watchedFilePath);
    }

    private void SaveSession(AICodingServicesSessionState session)
    {
        Directory.CreateDirectory(SessionRoot);
        File.WriteAllText(GetSessionPath(session.SessionId), JsonSerializer.Serialize(session, JsonOptions));
    }

    private AICodingServicesSessionFileAccess RecordSessionFileAccess(
        string sessionId,
        string sourceFilePath,
        string accessKind,
        AICodingServicesFileHashInfo hash)
    {
        AICodingServicesSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        List<AICodingServicesSessionFileAccess> files = session.Files.ToList();
        AICodingServicesSessionFileAccess? previous = files
            .FirstOrDefault(item => item.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase));
        AICodingServicesSessionFileAccess updated = new(
            sessionId,
            sourceFilePath,
            workflowPaths.GetRelativeWatchedPath(sourceFilePath),
            accessKind,
            hash,
            (previous?.FetchCount ?? 0) + 1,
            previous?.FirstAccessedAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        if (previous is not null)
        {
            files.Remove(previous);
        }

        files.Add(updated);
        SaveSession(session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Files = files
        });
        return updated;
    }

    private AICodingServicesSessionState? LoadSessionById(string sessionId)
    {
        string path = GetSessionPath(sessionId);
        return File.Exists(path) ? LoadSession(path) : null;
    }

    private AICodingServicesSessionState? LoadSession(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AICodingServicesSessionState>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetSessionPath(string sessionId)
    {
        return Path.Combine(SessionRoot, $"{Sanitize(sessionId)}.json");
    }

    private void RecordRoslynSessionEvent(string? sessionId, string eventType, RoslynEditResult result)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, eventType, result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private PlannedSessionDecisionOptions BuildPlannedSessionDecisionOptions(string stagedRecordId, string requestedDecision)
    {
        StagedEditRecord currentRecord = workflowService.GetStagedRecord(stagedRecordId);
        if (string.IsNullOrWhiteSpace(currentRecord.SessionId))
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        AICodingServicesSessionEditPlan? editPlan = LoadSessionById(currentRecord.SessionId)?.EditPlan;
        if (editPlan is null || editPlan.FilesPlanned.Count == 0)
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        IReadOnlyList<StagedEditRecord> sessionRecords = workflowService.ListStagedRecords(currentRecord.SessionId);
        HashSet<string> terminalPlannedPaths = sessionRecords
            .Where(record => !record.StagedRecordId.Equals(stagedRecordId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(record.Decision))
            .Select(record => Path.GetFullPath(record.WatchedFilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        terminalPlannedPaths.Add(Path.GetFullPath(currentRecord.WatchedFilePath));
        bool allPlannedFilesDecided = editPlan.FilesPlanned.All(file =>
            terminalPlannedPaths.Contains(Path.GetFullPath(file.SourceFilePath)));

        string normalizedDecision = requestedDecision.Trim().ToLowerInvariant();
        HashSet<string> acceptedPaths = sessionRecords
            .Where(record => !record.StagedRecordId.Equals(stagedRecordId, StringComparison.Ordinal)
                && record.Classification is "accepted" or "accepted-normalized")
            .Select(record => Path.GetFullPath(record.WatchedFilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedDecision.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            acceptedPaths.Add(Path.GetFullPath(currentRecord.WatchedFilePath));
        }

        AICodingServicesSessionPlannedFile[] acceptedPlannedFiles = editPlan.FilesPlanned
            .Where(file => acceptedPaths.Contains(Path.GetFullPath(file.SourceFilePath)))
            .ToArray();
        if (acceptedPlannedFiles.Length == 0)
        {
            return new PlannedSessionDecisionOptions(false, null, []);
        }

        PostAcceptIndexRefreshPlan refreshPlan = new()
        {
            ChangedFilePaths = acceptedPlannedFiles.Select(file => file.SourceFilePath).ToArray(),
            OwningProjectPaths = acceptedPlannedFiles.Select(file => file.OwningProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        StagedEditRecord[] terminalValidationRecords = !allPlannedFilesDecided
            ? []
            : sessionRecords
                .Append(currentRecord)
                .Where(record => acceptedPaths.Contains(Path.GetFullPath(record.WatchedFilePath)))
                .GroupBy(record => Path.GetFullPath(record.WatchedFilePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(record => record.CreatedAtUtc, StringComparer.Ordinal).First())
                .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        return new PlannedSessionDecisionOptions(!allPlannedFilesDecided, refreshPlan, terminalValidationRecords);
    }

    private bool ShouldDeferPlannedOverlayValidation(string? sessionId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        AICodingServicesSessionEditPlan? editPlan = RequireSessionEditPlan(sessionId);
        EnsurePlannedFile(editPlan, sourceFilePath);
        string currentPath = Path.GetFullPath(sourceFilePath);
        bool allPlannedWorkingFilesExist = editPlan.FilesPlanned.All(file =>
        {
            string plannedPath = Path.GetFullPath(file.SourceFilePath);
            if (plannedPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return File.Exists(workflowPaths.GetWorkingFilePath(plannedPath));
        });
        return !allPlannedWorkingFilesExist;
    }

    private void EnsurePlannedMutationAllowed(string? sessionId, string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session edit scope is required before MCP workflow mutations. Call start_monitor_session with filesPlanned before editing or staging.");
        }

        AICodingServicesSessionEditPlan editPlan = RequireSessionEditPlan(sessionId);
        EnsurePlannedFile(editPlan, sourceFilePath);
    }

    private bool ShouldDeferBuildValidationUntilAccept(StagedEditRecord stagedRecord)
    {
        if (string.IsNullOrWhiteSpace(stagedRecord.SessionId))
        {
            return false;
        }

        AICodingServicesSessionEditPlan editPlan = RequireSessionEditPlan(stagedRecord.SessionId);
        EnsurePlannedFile(editPlan, stagedRecord.WatchedFilePath);
        IReadOnlyList<StagedEditRecord> sessionRecords = workflowService.ListStagedRecords(stagedRecord.SessionId);
        foreach (AICodingServicesSessionPlannedFile plannedFile in editPlan.FilesPlanned)
        {
            string plannedPath = Path.GetFullPath(plannedFile.SourceFilePath);
            IEnumerable<StagedEditRecord> plannedFileRecords = sessionRecords.Where(record =>
                Path.GetFullPath(record.WatchedFilePath).Equals(plannedPath, StringComparison.OrdinalIgnoreCase));
            // Launch-deadlock fix: a planned file already carrying a final decision is
            // satisfied. Interleaving launch -> decide -> launch on the remaining files
            // must not throw just because an earlier file was decided and no longer has an
            // active (undecided) staged record. Only files NOT yet decided must still have
            // an active staged record.
            bool alreadyDecided = plannedFileRecords.Any(record => !string.IsNullOrWhiteSpace(record.Decision));
            if (alreadyDecided)
            {
                continue;
            }

            bool hasActiveStagedRecord = plannedFileRecords.Any(record =>
                string.IsNullOrWhiteSpace(record.Decision)
                && string.IsNullOrWhiteSpace(record.SupersededByStagedRecordId)
                && !record.Status.Equals("superseded", StringComparison.OrdinalIgnoreCase)
                && !record.Classification.Equals("superseded", StringComparison.OrdinalIgnoreCase));
            if (!hasActiveStagedRecord)
            {
                throw new InvalidOperationException("Cannot launch review until every planned session edit file has a staged record. Stage missing planned file: " + plannedFile.RelativePath);
            }
        }

        return true;
    }

    private AICodingServicesSessionEditPlan RequireSessionEditPlan(string sessionId)
    {
        AICodingServicesSessionState session = GetMonitorSession(sessionId);
        if (session.EditPlan is null || session.EditPlan.FilesPlanned.Count == 0)
        {
            throw new InvalidOperationException("Session edit plan is required before MCP workflow edits. Call start_monitor_session with filesPlanned before editing, staging, or launching review.");
        }

        return session.EditPlan;
    }

    private IReadOnlyList<AICodingServicesSessionPlannedFile> BuildPlannedFiles(IReadOnlyList<AICodingServicesSessionPlannedFileInput> filesPlanned)
    {
        if (filesPlanned.Count == 0)
        {
            throw new InvalidOperationException("At least one planned edit file is required.");
        }

        List<AICodingServicesSessionPlannedFile> plannedFiles = [];
        foreach (AICodingServicesSessionPlannedFileInput input in filesPlanned)
        {
            string sourceFilePath = ResolveWatchedPath(input.SourceFilePath);
            string owningProjectPath = string.IsNullOrWhiteSpace(input.OwningProjectPath)
                ? ResolveOwningProjectPath(sourceFilePath)
                : Path.GetFullPath(input.OwningProjectPath);
            if (plannedFiles.Any(file =>
                file.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase)
                && file.OwningProjectPath.Equals(owningProjectPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            plannedFiles.Add(new AICodingServicesSessionPlannedFile(
                sourceFilePath,
                workflowPaths.GetRelativeWatchedPath(sourceFilePath),
                owningProjectPath,
                Path.GetFileName(sourceFilePath),
                Path.GetFileName(owningProjectPath),
                string.IsNullOrWhiteSpace(input.Role) ? "edit" : input.Role,
                input.Reason ?? string.Empty));
        }

        return plannedFiles;
    }

    private static void EnsurePlannedFile(AICodingServicesSessionEditPlan editPlan, string sourceFilePath)
    {
        string fullPath = Path.GetFullPath(sourceFilePath);
        bool isPlanned = editPlan.FilesPlanned.Any(file =>
            Path.GetFullPath(file.SourceFilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (!isPlanned)
        {
            throw new InvalidOperationException("Source file is not in the session edit plan: " + fullPath);
        }
    }

    private string ResolveOwningProjectPath(string sourceFilePath)
    {
        string normalizedSourceFilePath = Path.GetFullPath(sourceFilePath);
        IndexedDocumentRow[] matches = queryService.ListDocuments(filePath: normalizedSourceFilePath)
            .Where(document => Path.GetFullPath(document.FilePath).Equals(normalizedSourceFilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Planned file must include owningProjectPath when the existing index does not have exactly one owning project for: " + sourceFilePath);
        }

        return Path.GetFullPath(matches[0].ProjectPath);
    }

    private static AICodingServicesFileHashInfo GetFileHashInfo(string path)
    {
        FileInfo info = new(path);
        return new AICodingServicesFileHashInfo(
            ComputeFileHash(path),
            info.Length,
            info.LastWriteTimeUtc);
    }

    private static AICodingServicesToolErrorResult? TryCreateIndexedStableSymbolKeyError(string stableSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return new AICodingServicesToolErrorResult(
                true,
                "A stable indexed symbol key is required. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol to obtain a symbol:<hash> key.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        if (stableSymbolKey.StartsWith("symbol:", StringComparison.Ordinal))
        {
            return null;
        }

        if (stableSymbolKey.Contains("::", StringComparison.Ordinal))
        {
            return new AICodingServicesToolErrorResult(
                true,
                "This looks like a Roslyn source-map selector key, not an indexed symbol key. find_indexed_references and find_indexed_callers require the symbol:<hash> key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        return new AICodingServicesToolErrorResult(
            true,
            "Indexed reference tools require a stable indexed symbol key in symbol:<hash> form. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol first.",
            "symbol:<hash>",
            stableSymbolKey);
    }

    private static AICodingServicesIndexedReferenceResult ToMcpReferenceRow(IndexedReferenceRow reference)
    {
        return new AICodingServicesIndexedReferenceResult(
            reference.TargetStableKey,
            reference.FilePath,
            reference.Line,
            reference.Column,
            reference.ReferenceKind,
            reference.Snippet,
            reference.TargetName,
            reference.TargetKind,
            reference.CallerStableKey,
            reference.CallerName,
            reference.CallerKind);
    }

    private static bool IsRecoverableRoslynGuidanceError(InvalidOperationException ex)
    {
        return ex.Message.Contains("Razor markup", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("supports C# source files only", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateExpectedHash(string path, string? expectedFileHash)
    {
        if (!string.IsNullOrWhiteSpace(expectedFileHash)
            && !ComputeFileHash(path).Equals(expectedFileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Working candidate hash did not match expectedFileHash.");
        }
    }

    private static bool IsUnderBuildOrHiddenDirectory(string path)
    {
        string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal)
            || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeFileHash(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        {
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    private static string ComputeHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "item" : clean;
    }
}
