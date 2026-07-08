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

    public EditSessionStatus RefreshFile(string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).Refresh(context.WatchedFilePath);
    }

    public EditSessionStatus NewFile(string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new WorkflowEditService(context.Settings).NewFile(context.WatchedFilePath);
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

    public RoslynFileOutlineResult GetFileOutline(string watchedFilePath)
    {
        WorkspaceEditContext context = ResolveContext(watchedFilePath);
        return new RoslynEditService(context.Settings).GetFileOutline(context.WatchedFilePath);
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
        string fullPath = Path.IsPathRooted(watchedFilePath)
            ? Path.GetFullPath(watchedFilePath)
            : Path.GetFullPath(Path.Combine(fullRepoRoot, watchedFilePath));

        if (!IsWithinWorkspace(fullRepoRoot, fullPath))
        {
            throw new InvalidOperationException($"Watched file path must stay within the selected workspace: {watchedFilePath}");
        }

        CodingServicesSettings settings = settingsProvider.GetSettings(fullRepoRoot);
        return new WorkspaceEditContext(fullRepoRoot, fullPath, settings);
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
}
