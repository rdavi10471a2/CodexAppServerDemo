using System.ComponentModel;
using CodexAppServerBlazor.AICodingServices.Workflow;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class WorkspaceEditMcpTools
{
    private readonly HarnessWorkspaceEditService workspaceEditService;

    public WorkspaceEditMcpTools(HarnessWorkspaceEditService workspaceEditService)
    {
        this.workspaceEditService = workspaceEditService;
    }

    [McpServerTool]
    [Description("Returns the current governed edit-session status for a workspace file path. Accepts a path relative to the selected workspace CWD or an absolute path inside that workspace.")]
    public EditSessionStatus GetEditSessionState(string watchedFilePath)
    {
        return workspaceEditService.GetEditSessionState(watchedFilePath);
    }

    [McpServerTool]
    [Description("Creates or refreshes the governed Working candidate for an existing workspace file. Refresh starts a clean governed edit pass for that file: it recreates the Working candidate from watched source and retires older pending staged review records for the same file. Pass sessionId to keep multiple files in one governed edit session for a coherent task.")]
    public EditSessionStatus RefreshFile(string watchedFilePath, string? sessionId = null)
    {
        return workspaceEditService.RefreshFile(watchedFilePath, sessionId);
    }

    [McpServerTool]
    [Description("Creates a new-file governed edit session for a file path that does not yet exist inside the selected workspace. Pass sessionId to keep multiple files in one governed edit session for a coherent task.")]
    public EditSessionStatus NewFile(string watchedFilePath, string? sessionId = null)
    {
        return workspaceEditService.NewFile(watchedFilePath, sessionId);
    }

    [McpServerTool]
    [Description("Declares the full governed file set for an existing edit session before any multi-file staging. Use this when a Work task intentionally spans more than one file.")]
    public EditSessionPlan DeclareSessionFiles(string sessionId, IEnumerable<string> watchedFilePaths)
    {
        return workspaceEditService.DeclareSessionFiles(sessionId, watchedFilePaths);
    }

    [McpServerTool]
    [Description("Adds one watched workspace file to an existing governed edit session and returns the updated declared file set. Prefer this after refresh_file/new_file when a coherent task grows from one file to multiple files incrementally.")]
    public EditSessionPlan AddFileToSession(string sessionId, string watchedFilePath)
    {
        return workspaceEditService.AddFileToSession(sessionId, watchedFilePath);
    }

    [McpServerTool]
    [Description("Replaces text inside the governed Working candidate for a workspace file. Supports expected match counts, working-hash guards, and optional overlay validation.")]
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
        return workspaceEditService.ReplaceTextInFile(
            watchedFilePath,
            oldText,
            newText,
            expectedMatches,
            expectedWorkingHash,
            occurrenceIndex,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Replaces a line/column span inside the governed Working candidate for a workspace file. Supports working-hash guards, expected old-text guards, and optional overlay validation.")]
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
        return workspaceEditService.ReplaceSpanInFile(
            watchedFilePath,
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

    [McpServerTool]
    [Description("Returns a Roslyn outline for a C# source file. Use this for quick structure discovery before targeting symbol-level reads or edits.")]
    public RoslynFileOutlineResult GetFileOutline(string watchedFilePath)
    {
        return workspaceEditService.GetFileOutline(watchedFilePath);
    }

    [McpServerTool]
    [Description("Returns a Roslyn source map for C# files in the selected workspace. Source-map symbols now include dependency-injection registration hints when the workspace registers them through IServiceCollection patterns such as AddScoped, AddSingleton, AddTransient, TryAdd, keyed registrations, or ServiceDescriptor factories.")]
    public RoslynSourceMapResult GetSourceMap(string? path, string scope = "auto", string mode = "auto", string? namespaceName = null)
    {
        return workspaceEditService.GetSourceMap(path, scope, mode, namespaceName);
    }

    [McpServerTool]
    [Description("Reads a single symbol body from a C# source file in the governed Working candidate using a Roslyn symbol selector JSON payload.")]
    public RoslynSymbolReadResult GetSymbol(string watchedFilePath, string symbolSelectorJson)
    {
        return workspaceEditService.GetSymbol(watchedFilePath, symbolSelectorJson);
    }

    [McpServerTool]
    [Description("Replaces a single symbol in a C# source file in the governed Working candidate using a Roslyn symbol selector JSON payload and replacement code.")]
    public RoslynEditResult SubmitSymbol(
        string watchedFilePath,
        string symbolSelectorJson,
        string code,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.SubmitSymbol(
            watchedFilePath,
            symbolSelectorJson,
            code,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a field to a containing C# type in the governed Working candidate.")]
    public RoslynEditResult AddField(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddField(
            watchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a property to a containing C# type in the governed Working candidate.")]
    public RoslynEditResult AddProperty(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddProperty(
            watchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a method to a containing C# type in the governed Working candidate.")]
    public RoslynEditResult AddMethod(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddMethod(
            watchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a constructor to a containing C# type in the governed Working candidate.")]
    public RoslynEditResult AddConstructor(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddConstructor(
            watchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a nested type to a containing C# type in the governed Working candidate.")]
    public RoslynEditResult AddNestedType(
        string watchedFilePath,
        string containingType,
        string declaration,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddNestedType(
            watchedFilePath,
            containingType,
            declaration,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds or removes the partial modifier on a containing C# type in the governed Working candidate.")]
    public RoslynEditResult SetTypePartial(
        string watchedFilePath,
        string containingType,
        bool isPartial,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.SetTypePartial(
            watchedFilePath,
            containingType,
            isPartial,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a using directive to a C# source file in the governed Working candidate.")]
    public RoslynEditResult AddUsing(
        string watchedFilePath,
        string namespaceName,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddUsing(
            watchedFilePath,
            namespaceName,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Removes a using directive from a C# source file in the governed Working candidate.")]
    public RoslynEditResult RemoveUsing(
        string watchedFilePath,
        string namespaceName,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.RemoveUsing(
            watchedFilePath,
            namespaceName,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Removes a single symbol from a C# source file in the governed Working candidate using a Roslyn symbol selector JSON payload.")]
    public RoslynEditResult RemoveSymbol(
        string watchedFilePath,
        string symbolSelectorJson,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.RemoveSymbol(
            watchedFilePath,
            symbolSelectorJson,
            manifestJson,
            validateOverlay);
    }
}
