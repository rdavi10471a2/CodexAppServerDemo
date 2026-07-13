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
    [Description("Returns the current governed edit-session status for one required workspace file path (`watchedFilePath`). `watchedFilePath` may be relative to the selected workspace CWD or an absolute path inside that workspace. Diagnostic only: this does not create or refresh a governed Working candidate and is not sufficient evidence for an 'already implemented' or 'already complete' conclusion in Work mode.")]
    public EditSessionStatus GetEditSessionState(string watchedFilePath)
    {
        return workspaceEditService.GetEditSessionState(watchedFilePath);
    }

    [McpServerTool]
    [Description("Creates or refreshes the governed Working candidate for one required existing workspace file (`watchedFilePath`). This is the normal governed entry point for edits to an existing watched file. Optional `sessionId` explicitly keeps the file inside one coherent governed edit session. Refresh starts a clean governed edit pass for that file: it recreates the Working candidate from watched source and retires older pending staged review records for the same file.")]
    public EditSessionStatus RefreshFile(string watchedFilePath, string? sessionId = null)
    {
        return workspaceEditService.RefreshFile(watchedFilePath, sessionId);
    }

    [McpServerTool]
    [Description("Creates a new-file governed Working candidate for one required workspace file path (`watchedFilePath`) that does not yet exist inside the selected workspace. This is the normal governed entry point for new watched files. Optional `sessionId` explicitly keeps the file inside one coherent governed edit session.")]
    public EditSessionStatus NewFile(string watchedFilePath, string? sessionId = null)
    {
        return workspaceEditService.NewFile(watchedFilePath, sessionId);
    }

    [McpServerTool]
    [Description("Advanced bulk declaration or replacement of the full governed file set for an existing edit session before multi-file staging. Required arguments: `sessionId` and non-empty `watchedFilePaths`. Every declared file must already be bootstrapped into that same governed edit session through `refresh_file(..., sessionId)` or `new_file(..., sessionId)`. Do not use this as the normal session-growth path; prefer `add_file_to_session` when a coherent task grows one file at a time.")]
    public EditSessionPlan DeclareSessionFiles(string sessionId, string[] watchedFilePaths)
    {
        return workspaceEditService.DeclareSessionFiles(sessionId, watchedFilePaths);
    }

    [McpServerTool]
    [Description("Adds one watched workspace file to an existing governed edit session and returns the updated declared file set. Required arguments: `sessionId` and `watchedFilePath`. This is the normal session-growth path after `refresh_file(..., sessionId)` or `new_file(..., sessionId)` when a coherent task expands incrementally from one file to multiple files.")]
    public EditSessionPlan AddFileToSession(string sessionId, string watchedFilePath)
    {
        return workspaceEditService.AddFileToSession(sessionId, watchedFilePath);
    }

    [McpServerTool]
    [Description("Replaces text inside the governed Working candidate for one required workspace file (`watchedFilePath`). Required arguments: `watchedFilePath`, `oldText`, and `newText`. Prefer this for one contiguous unique text replacement in an already refreshed file. Optional guards include `expectedMatches`, `expectedWorkingHash`, `occurrenceIndex`, and overlay validation.")]
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
    [Description("Replaces a line/column span inside the governed Working candidate for one required workspace file (`watchedFilePath`). Required arguments: `watchedFilePath`, `startLine`, `startColumn`, `endLine`, `endColumn`, and `newText`. Coordinate-based fallback only: prefer symbol-aware edits or `replace_text_in_file` when they can express the change safely. Span replacement is destructive as a planning surface if further edits are intended in the same file unless coordinates are regenerated from the current Working candidate immediately before each edit. Optional guards include `expectedWorkingHash`, `expectedOldTextHash`, `expectedOldText`, and overlay validation.")]
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
    [Description("Replaces the entire governed Working candidate for one required workspace file (`watchedFilePath`) with one required complete file body (`content`). Use this for brand-new watched files, generated files, or deliberate whole-file replacement. Prefer narrower governed edits such as `submit_symbol` or `replace_text_in_file` when they fit. This tool does not take `sessionId`: it writes the Working candidate already bound to `watchedFilePath`, so establish or reuse the session first through `refresh_file` or `new_file`. For new-file authoring, call `new_file` first, then `submit_file`.")]
    public EditSessionStatus SubmitFile(
        string watchedFilePath,
        string content,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.SubmitFile(
            watchedFilePath,
            content,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Returns a Roslyn outline for one required C# source file (`watchedFilePath`). Use this for quick structure discovery before targeting symbol-level reads or edits.")]
    public RoslynFileOutlineResult GetFileOutline(string watchedFilePath)
    {
        return workspaceEditService.GetFileOutline(watchedFilePath);
    }

    [McpServerTool]
    [Description("Returns a Roslyn source map for C# files in the selected workspace. Optional arguments: `path`, `scope`, `mode`, and `namespaceName`. Use `path` when you already know the file; use namespace scope when you need nearby type discovery. This is indexed structure discovery, not a source-body read. Source-map symbols also include dependency-injection registration hints when the workspace registers them through IServiceCollection patterns such as AddScoped, AddSingleton, AddTransient, TryAdd, keyed registrations, or ServiceDescriptor factories.")]
    public RoslynSourceMapResult GetSourceMap(string? path, string scope = "auto", string mode = "auto", string? namespaceName = null)
    {
        return workspaceEditService.GetSourceMap(path, scope, mode, namespaceName);
    }

    [McpServerTool]
    [Description("Reads a single symbol body from one required C# source file (`watchedFilePath`) in the governed Working candidate using one required Roslyn selector payload (`symbolSelectorJson`). Use `get_file_outline` or `get_source_map` first when the selector shape is unclear.")]
    public RoslynSymbolReadResult GetSymbol(string watchedFilePath, string symbolSelectorJson)
    {
        return workspaceEditService.GetSymbol(watchedFilePath, symbolSelectorJson);
    }

    [McpServerTool]
    [Description("Replaces a single symbol in one required C# source file (`watchedFilePath`) in the governed Working candidate using one required Roslyn selector payload (`symbolSelectorJson`) and one required replacement body (`code`). Prefer this for multi-fragment logical edits that stay within one symbol body; it is safer than stacking span replacements when further edits are likely in the same file.")]
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
    [Description("Adds one symbol of a required `symbolType` to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, `symbolType`, and symbol `code`. Use the more specific typed add tools when they fit; use `add_symbol` when the symbol kind is governed but does not fit the narrower typed helpers.")]
    public RoslynEditResult AddSymbol(
        string watchedFilePath,
        string containingType,
        string symbolType,
        string code,
        string? afterSymbol = null,
        string? manifestJson = null,
        bool validateOverlay = true)
    {
        return workspaceEditService.AddSymbol(
            watchedFilePath,
            containingType,
            symbolType,
            code,
            afterSymbol,
            manifestJson,
            validateOverlay);
    }

    [McpServerTool]
    [Description("Adds a field to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and field `declaration`. Prefer the simple containing type name from the file first (for example `HelpSubject`) if a fully qualified name does not resolve.")]
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
    [Description("Adds a property to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and property `declaration`. Prefer the simple containing type name from the file first (for example `HelpSubject`) if a fully qualified name does not resolve.")]
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
    [Description("Adds a method to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and method `declaration`. Prefer the simple containing type name from the file first (for example `HelpSubject`) if a fully qualified name does not resolve.")]
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
    [Description("Adds a constructor to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and constructor `declaration`. Prefer the simple containing type name from the file first if a fully qualified name does not resolve.")]
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
    [Description("Adds a nested type to one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and nested-type `declaration`. Prefer the simple containing type name from the file first if a fully qualified name does not resolve.")]
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
    [Description("Adds or removes the partial modifier on one required containing C# type in the governed Working candidate. Required arguments: `watchedFilePath`, `containingType`, and `isPartial`. Prefer the simple containing type name from the file first if a fully qualified name does not resolve.")]
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
    [Description("Adds a using directive to one required C# source file in the governed Working candidate. Required arguments: `watchedFilePath` and `namespaceName`.")]
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
    [Description("Removes a using directive from one required C# source file in the governed Working candidate. Required arguments: `watchedFilePath` and `namespaceName`.")]
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
    [Description("Removes a single symbol from one required C# source file (`watchedFilePath`) in the governed Working candidate using one required Roslyn selector payload (`symbolSelectorJson`). This is the preferred governed path for C# symbol deletion. Use `get_file_outline` or `get_source_map` first when the selector shape is unclear.")]
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
