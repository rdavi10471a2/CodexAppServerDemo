using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Governed edit operations exposed through the local MCP boundary for the currently selected workspace.
/// </summary>
public sealed class HarnessWorkspaceEditService
{
    private readonly WorkspaceState workspaceState;
    private readonly CodingServicesSettingsProvider settingsProvider;

    public HarnessWorkspaceEditService(WorkspaceState workspaceState, CodingServicesSettingsProvider settingsProvider)
    {
        this.workspaceState = workspaceState;
        this.settingsProvider = settingsProvider;
    }

    public EditSessionStatus GetEditSessionState(string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).GetStatus(context.WatchedFilePath);
    }

    public EditSessionStatus RefreshFile(string watchedFilePath, string? sessionId = null)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        WorkflowEditService workflowService = new(context.Settings);
        WorkflowRunRecorder recorder = CreateRecorder(context.Settings, "refresh-file", out string runId);
        recorder.Stage(runId, "mcp-refresh-file-start", new Dictionary<string, string>
        {
            ["workspaceRoot"] = context.WorkspaceRoot,
            ["watchedFilePath"] = context.WatchedFilePath,
            ["requestedSessionId"] = sessionId ?? "<null>",
            ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
        });

        try
        {
            string? resolvedSessionId = ResolveRefreshSessionId(workflowService, context.WatchedFilePath, sessionId);
            EditSessionStatus status = workflowService.Refresh(context.WatchedFilePath, resolvedSessionId);
            workspaceState.SetCurrentEditSessionId(status.EditSessionId);
            recorder.Stage(runId, "mcp-refresh-file-success", new Dictionary<string, string>
            {
                ["resolvedSessionId"] = resolvedSessionId ?? "<new-session>",
                ["resultSessionId"] = status.EditSessionId,
                ["classification"] = status.Classification,
                ["workingFilePath"] = status.WorkingFilePath
            });
            return status;
        }
        catch (Exception ex)
        {
            recorder.Stage(runId, "mcp-refresh-file-failure", BuildFailureFields(ex, new Dictionary<string, string>
            {
                ["workspaceRoot"] = context.WorkspaceRoot,
                ["watchedFilePath"] = context.WatchedFilePath,
                ["requestedSessionId"] = sessionId ?? "<null>",
                ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
            }));
            throw;
        }
    }

    public EditSessionStatus NewFile(string watchedFilePath, string? sessionId = null)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        WorkflowEditService workflowService = new(context.Settings);
        WorkflowRunRecorder recorder = CreateRecorder(context.Settings, "new-file", out string runId);
        recorder.Stage(runId, "mcp-new-file-start", new Dictionary<string, string>
        {
            ["workspaceRoot"] = context.WorkspaceRoot,
            ["watchedFilePath"] = context.WatchedFilePath,
            ["requestedSessionId"] = sessionId ?? "<null>",
            ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
        });

        try
        {
            string? resolvedSessionId = ResolveRefreshSessionId(workflowService, context.WatchedFilePath, sessionId);
            EditSessionStatus status = workflowService.NewFile(context.WatchedFilePath, resolvedSessionId);
            workspaceState.SetCurrentEditSessionId(status.EditSessionId);
            recorder.Stage(runId, "mcp-new-file-success", new Dictionary<string, string>
            {
                ["resolvedSessionId"] = resolvedSessionId ?? "<new-session>",
                ["resultSessionId"] = status.EditSessionId,
                ["classification"] = status.Classification,
                ["workingFilePath"] = status.WorkingFilePath
            });
            return status;
        }
        catch (Exception ex)
        {
            recorder.Stage(runId, "mcp-new-file-failure", BuildFailureFields(ex, new Dictionary<string, string>
            {
                ["workspaceRoot"] = context.WorkspaceRoot,
                ["watchedFilePath"] = context.WatchedFilePath,
                ["requestedSessionId"] = sessionId ?? "<null>",
                ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
            }));
            throw;
        }
    }

    public EditSessionPlan DeclareSessionFiles(string sessionId, IEnumerable<string> watchedFilePaths)
    {
        WorkspaceEditContext workspace = ResolveWorkspace();
        WorkflowEditService workflowService = new(workspace.Settings);
        List<string> resolvedPaths = watchedFilePaths
            .Select(ResolveContext)
            .Select(context => context.WatchedFilePath)
            .ToList();
        WorkflowRunRecorder recorder = CreateRecorder(workspace.Settings, "declare-session-files", out string runId);
        recorder.Stage(runId, "mcp-declare-session-files-start", new Dictionary<string, string>
        {
            ["workspaceRoot"] = workspace.WorkspaceRoot,
            ["sessionId"] = sessionId,
            ["resolvedPathCount"] = resolvedPaths.Count.ToString(),
            ["resolvedPaths"] = string.Join(" | ", resolvedPaths.Select(workflowService.GetRelativePathForDiagnostics)),
            ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
        });

        try
        {
            EditSessionPlan plan = workflowService.DeclareSessionFiles(sessionId, resolvedPaths);
            workspaceState.SetCurrentEditSessionId(plan.SessionId);
            recorder.Stage(runId, "mcp-declare-session-files-success", new Dictionary<string, string>
            {
                ["sessionId"] = plan.SessionId,
                ["declaredPathCount"] = plan.DeclaredWatchedFilePaths.Count.ToString(),
                ["declaredPaths"] = string.Join(" | ", plan.DeclaredRelativePaths)
            });
            return plan;
        }
        catch (Exception ex)
        {
            EditSessionPlan? survivingPlan = workflowService.GetSessionPlan(sessionId);
            recorder.Stage(runId, "mcp-declare-session-files-failure", BuildFailureFields(ex, new Dictionary<string, string>
            {
                ["workspaceRoot"] = workspace.WorkspaceRoot,
                ["sessionId"] = sessionId,
                ["resolvedPathCount"] = resolvedPaths.Count.ToString(),
                ["resolvedPaths"] = string.Join(" | ", resolvedPaths.Select(workflowService.GetRelativePathForDiagnostics)),
                ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>",
                ["survivingPlan"] = survivingPlan is null
                    ? "<null>"
                    : string.Join(" | ", survivingPlan.DeclaredRelativePaths)
            }));
            throw;
        }
    }

    public EditSessionPlan AddFileToSession(string sessionId, string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        WorkflowEditService workflowService = new(context.Settings);
        WorkflowRunRecorder recorder = CreateRecorder(context.Settings, "add-file-to-session", out string runId);
        recorder.Stage(runId, "mcp-add-file-to-session-start", new Dictionary<string, string>
        {
            ["workspaceRoot"] = context.WorkspaceRoot,
            ["sessionId"] = sessionId,
            ["watchedFilePath"] = context.WatchedFilePath,
            ["relativePath"] = workflowService.GetRelativePathForDiagnostics(context.WatchedFilePath),
            ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>"
        });

        try
        {
            EditSessionPlan plan = workflowService.AddFileToSession(sessionId, context.WatchedFilePath);
            workspaceState.SetCurrentEditSessionId(plan.SessionId);
            recorder.Stage(runId, "mcp-add-file-to-session-success", new Dictionary<string, string>
            {
                ["sessionId"] = plan.SessionId,
                ["declaredPathCount"] = plan.DeclaredWatchedFilePaths.Count.ToString(),
                ["declaredPaths"] = string.Join(" | ", plan.DeclaredRelativePaths)
            });
            return plan;
        }
        catch (Exception ex)
        {
            EditSessionPlan? survivingPlan = workflowService.GetSessionPlan(sessionId);
            recorder.Stage(runId, "mcp-add-file-to-session-failure", BuildFailureFields(ex, new Dictionary<string, string>
            {
                ["workspaceRoot"] = context.WorkspaceRoot,
                ["sessionId"] = sessionId,
                ["watchedFilePath"] = context.WatchedFilePath,
                ["relativePath"] = workflowService.GetRelativePathForDiagnostics(context.WatchedFilePath),
                ["currentWorkspaceSessionId"] = workspaceState.CurrentEditSessionId ?? "<null>",
                ["survivingPlan"] = survivingPlan is null
                    ? "<null>"
                    : string.Join(" | ", survivingPlan.DeclaredRelativePaths)
            }));
            throw;
        }
    }

    public ReplaceTextResult ReplaceTextInFile(
        string watchedFilePath,
        string oldText,
        string newText,
        int? expectedMatches = null,
        string? expectedWorkingHash = null,
        int? occurrenceIndex = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).ReplaceText(
            context.WatchedFilePath,
            oldText,
            newText,
            expectedMatches,
            expectedWorkingHash,
            occurrenceIndex,
            manifestJson,
            validateOverlay);
    }

    public EditSessionStatus ReplaceSpanInFile(
        string watchedFilePath,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        string newText,
        string? expectedWorkingHash = null,
        string? expectedOldTextHash = null,
        string? expectedOldText = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).ReplaceSpan(
            context.WatchedFilePath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            newText,
            expectedWorkingHash,
            expectedOldTextHash,
            expectedOldText,
            manifestJson,
            validateOverlay);
    }

    public EditSessionStatus SubmitFile(
        string watchedFilePath,
        string content,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).SubmitFile(
            context.WatchedFilePath,
            content,
            manifestJson,
            validateOverlay);
    }

    public RoslynFileOutlineResult GetFileOutline(string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).GetFileOutline(context.WatchedFilePath);
    }

    public RoslynSourceMapResult GetSourceMap(string? path, string scope = "auto", string mode = "auto", string? namespaceName = null)
    {
        WorkspaceEditContext workspace = ResolveWorkspace();
        return new RoslynEditService(workspace.Settings).GetSourceMap(path, scope, mode, namespaceName);
    }

    public RoslynSymbolReadResult GetSymbol(string watchedFilePath, string symbolSelectorJson)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).GetSymbol(context.WatchedFilePath, symbolSelectorJson);
    }

    public RoslynEditResult SubmitSymbol(
        string watchedFilePath,
        string symbolSelectorJson,
        string code,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).SubmitSymbol(
            context.WatchedFilePath,
            symbolSelectorJson,
            code,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddSymbol(
        string watchedFilePath,
        string containingType,
        string symbolType,
        string code,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddSymbol(
            context.WatchedFilePath,
            containingType,
            symbolType,
            code,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddField(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddField(
            context.WatchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddProperty(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddProperty(
            context.WatchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddMethod(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddMethod(
            context.WatchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddConstructor(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddConstructor(
            context.WatchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddNestedType(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddNestedType(
            context.WatchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult SetTypePartial(
        string watchedFilePath,
        string containingType,
        bool isPartial,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).SetTypePartial(
            context.WatchedFilePath,
            containingType,
            isPartial,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult AddUsing(
        string watchedFilePath,
        string namespaceName,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).AddUsing(
            context.WatchedFilePath,
            namespaceName,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult RemoveUsing(
        string watchedFilePath,
        string namespaceName,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).RemoveUsing(
            context.WatchedFilePath,
            namespaceName,
            manifestJson,
            validateOverlay);
    }

    public RoslynEditResult RemoveSymbol(
        string watchedFilePath,
        string symbolSelectorJson,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).RemoveSymbol(
            context.WatchedFilePath,
            symbolSelectorJson,
            manifestJson,
            validateOverlay);
    }

    private WorkspaceEditContext ResolveContext(string watchedFilePath)
    {
        WorkspaceEditContext workspace = ResolveWorkspace();
        string fullRepoRoot = workspace.WorkspaceRoot;
        string fullPath = Path.IsPathRooted(watchedFilePath)
            ? Path.GetFullPath(watchedFilePath)
            : Path.GetFullPath(Path.Combine(fullRepoRoot, watchedFilePath));

        if (!IsWithinWorkspace(fullRepoRoot, fullPath))
        {
            throw new InvalidOperationException($"Watched file path must stay within the selected workspace: {watchedFilePath}");
        }
        return workspace with { WatchedFilePath = fullPath };
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

    private sealed record WorkspaceEditContext(
        string WorkspaceRoot,
        string WatchedFilePath,
        CodingServicesSettings Settings);

    private WorkspaceEditContext ResolveWorkspace()
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
        CodingServicesSettings settings = settingsProvider.GetSettings(fullRepoRoot);
        return new WorkspaceEditContext(fullRepoRoot, string.Empty, settings);
    }

    private string? ResolveRefreshSessionId(
        WorkflowEditService workflowService,
        string watchedFilePath,
        string? requestedSessionId)
    {
        if (!string.IsNullOrWhiteSpace(requestedSessionId))
        {
            return requestedSessionId.Trim();
        }

        string? currentSessionId = workspaceState.CurrentEditSessionId;
        if (string.IsNullOrWhiteSpace(currentSessionId))
        {
            return null;
        }

        EditSessionStatus currentStatus = workflowService.GetStatus(watchedFilePath);
        if (currentStatus.HasSession
            && currentStatus.EditSessionId.Equals(currentSessionId, StringComparison.Ordinal))
        {
            return currentSessionId;
        }

        if (currentStatus.HasSession
            && !string.IsNullOrWhiteSpace(currentStatus.EditSessionId)
            && !currentStatus.EditSessionId.Equals(currentSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"An active governed edit session '{currentSessionId}' is already selected, but '{currentStatus.RelativePath}' is currently bound to '{currentStatus.EditSessionId}'. Pass the intended sessionId explicitly or retire the stale session before continuing.");
        }

        throw new InvalidOperationException(
            $"An active governed edit session '{currentSessionId}' is already selected. Pass that sessionId explicitly when refreshing an additional file for the same coherent task.");
    }

    private static WorkflowRunRecorder CreateRecorder(
        CodingServicesSettings settings,
        string operationName,
        out string runId)
    {
        WorkflowEditPaths paths = new(settings);
        runId = $"{operationName}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}";
        return new WorkflowRunRecorder(paths.HistoryRoot, runId);
    }

    private static Dictionary<string, string> BuildFailureFields(Exception exception, Dictionary<string, string> fields)
    {
        fields["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        fields["exceptionMessage"] = exception.Message;
        fields["exceptionStack"] = exception.ToString();
        return fields;
    }
}
