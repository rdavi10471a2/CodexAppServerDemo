// =============================================================================
// WorkspaceMcpTools.cs - ADDITIONS
// Part of: CodeHands_Implementation_Proposal.md
// Location: docs/proposed/
// Target: CodexAppServerBlazor/Mcp/WorkspaceMcpTools.cs
// Description: Add new workflow MCP tools to existing tools
// Dependencies: 
//   - CodexAppServerBlazor.AICodingServices/Workflow/WorkflowEditService
//   - CodexAppServerBlazor.AICodingServices/Workflow/RoslynEditService
//   - CodexAppServerBlazor.AICodingServices/Data/SolutionIndexQueryService
// =============================================================================

// ADD these new methods to the existing WorkspaceMcpTools class:

using System.ComponentModel;
using ModelContextProtocol.Server;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class WorkspaceMcpTools
{
    // EXISTING DEPENDENCY:
    private readonly HarnessWorkspaceContextService workspaceContextService;
    
    // ADD THESE DEPENDENCIES:
    private readonly FileCapabilitiesAnalyzer _fileCapabilitiesAnalyzer;
    private readonly WorkflowEditService _editService;
    private readonly RoslynEditService _roslynEditService;
    private readonly SolutionIndexQueryService _queryService;

    // UPDATE CONSTRUCTOR:
    public WorkspaceMcpTools(
        HarnessWorkspaceContextService workspaceContextService,
        FileCapabilitiesAnalyzer fileCapabilitiesAnalyzer,        // ADD
        WorkflowEditService editService,                        // ADD
        RoslynEditService roslynEditService,                   // ADD
        SolutionIndexQueryService queryService)                // ADD
    {
        this.workspaceContextService = workspaceContextService;
        _fileCapabilitiesAnalyzer = fileCapabilitiesAnalyzer;
        _editService = editService;
        _roslynEditService = roslynEditService;
        _queryService = queryService;
    }

    // =============================================================================
    // EXISTING TOOLS (keep as-is)
    // =============================================================================
    
    [McpServerTool]
    [Description("Returns metadata for the workspace CWD selected in the Coding Services Blazor control surface.")]
    public Task<WorkspaceResult> GetWorkspace(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWorkspaceAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns cheap watched-solution readiness metadata, counts, and a summary hash so an agent can decide whether to reload deeper context.")]
    public Task<WatchedSolutionDigestResult> GetWatchedSolutionDigest(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWatchedSolutionDigestAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the indexed watched-solution project/file/type/member tree for on-demand agent discovery. Does not include source file bodies.")]
    public Task<WatchedSolutionSummaryResult> GetWatchedSolutionSummary(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetWatchedSolutionSummaryAsync(cancellationToken);
    }

    [McpServerTool]
    [Description("Returns the indexed project/file/type/member tree for configured test projects only. Does not include source file bodies.")]
    public Task<WatchedSolutionSummaryResult> GetTestProjectSummary(CancellationToken cancellationToken = default)
    {
        return workspaceContextService.GetTestProjectSummaryAsync(cancellationToken);
    }

    // =============================================================================
    // NEW WORKFLOW TOOLS
    // =============================================================================

    // --- File Capabilities ---

    [McpServerTool]
    [Description("Analyze file capabilities for the CodeHands workflow. Returns file kind and allowed MCP tools.")]
    public FileCapabilitiesResult AnalyzeFileCapabilities(
        [Description("Source file path, absolute or relative to the workspace.")] string filePath)
    {
        var caps = _fileCapabilitiesAnalyzer.GetCapabilities(filePath);
        return new FileCapabilitiesResult(
            filePath,
            caps.Kind.ToString(),
            caps.AllowedTools.ToList(),
            caps.Warnings.ToList());
    }

    // --- Discovery Tools (Semantic Search) ---

    [McpServerTool]
    [Description("Find indexed C# symbols by name text, optional kind, optional exact namespace, and optional containing type.")]
    public IndexedSymbolSearchResult FindIndexedSymbols(
        [Description("Symbol name text to search for.")] string text,
        [Description("Optional symbol kind: class, method, property, field, constructor, enum, delegate, interface, struct, or record.")] string? kind = null,
        [Description("Optional exact namespace filter.")] string? namespaceName = null,
        [Description("Optional containing type filter.")] string? containingType = null,
        [Description("Maximum symbols to return.")] int maxResults = 100)
    {
        return _queryService.FindSymbols(text, kind, namespaceName, containingType, maxResults);
    }

    [McpServerTool]
    [Description("Find source or related files under the watched project folder by filename or wildcard pattern.")]
    public IReadOnlyList<FileMatchResult> FindFile(
        [Description("Filename or wildcard pattern, such as Program.cs or *.razor.")] string fileNameOrPattern,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        // Implementation: use Directory.EnumerateFiles with pattern
        throw new NotImplementedException("Implementation needed");
    }

    // --- Edit Tools (Text-Based) ---

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder and clear candidate state for that file.")]
    public EditSessionStatus RefreshFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return _editService.Refresh(sourceFilePath);
    }

    [McpServerTool]
    [Description("Create a new-file edit session with an empty monitor-owned Working candidate.")]
    public EditSessionStatus NewFile(
        [Description("Future watched source path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        return _editService.NewFile(sourceFilePath);
    }

    [McpServerTool]
    [Description("Write a full-file candidate into the monitor-owned Working mirror.")]
    public EditSessionStatus SubmitFile(
        [Description("Source file path.")] string path,
        [Description("Complete replacement file content.")] string content)
    {
        throw new NotImplementedException("Wrapper needed around WorkflowEditService.SubmitFile");
    }

    [McpServerTool]
    [Description("Replace exact oldText in the monitor-owned Working mirror candidate.")]
    public ReplaceTextResult ReplaceTextInFile(
        [Description("Source file path.")] string path,
        [Description("Exact old text to replace.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("0-based occurrence index. Leave unset for unique replacement.")] int occurrenceIndex = -1)
    {
        throw new NotImplementedException("Wrapper needed around WorkflowEditService.ReplaceText");
    }

    // --- Edit Tools (Roslyn Semantic) ---

    [McpServerTool]
    [Description("Replace one C# symbol in the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult SubmitSymbol(
        [Description("Source file path.")] string path, 
        [Description("Roslyn selector JSON.")] string symbolSelectorJson, 
        [Description("Replacement code.")] string code)
    {
        return _roslynEditService.SubmitSymbol(path, symbolSelectorJson, code, null, true);
    }

    [McpServerTool]
    [Description("Add a C# field to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddField(
        [Description("Source file path.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Field declaration.")] string declaration,
        [Description("Optional symbol to insert after.")] string? afterSymbol = null)
    {
        return _roslynEditService.AddField(path, containingType, declaration, afterSymbol, null, true);
    }

    [McpServerTool]
    [Description("Add a C# property to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddProperty(
        [Description("Source file path.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Property declaration.")] string declaration,
        [Description("Optional symbol to insert after.")] string? afterSymbol = null)
    {
        return _roslynEditService.AddProperty(path, containingType, declaration, afterSymbol, null, true);
    }

    [McpServerTool]
    [Description("Add a C# method to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddMethod(
        [Description("Source file path.")] string path,
        [Description("Containing type name.")] string containingType,
        [Description("Method declaration.")] string declaration,
        [Description("Optional symbol to insert after.")] string? afterSymbol = null)
    {
        return _roslynEditService.AddMethod(path, containingType, declaration, afterSymbol, null, true);
    }

    [McpServerTool]
    [Description("Add a using directive to the monitor-owned Working candidate.")]
    public RoslynEditResult AddUsing(
        [Description("Source file path.")] string path,
        [Description("Namespace to add.")] string @namespace)
    {
        return _roslynEditService.AddUsing(path, @namespace, null, true);
    }

    [McpServerTool]
    [Description("Remove a using directive from the monitor-owned Working candidate.")]
    public RoslynEditResult RemoveUsing(
        [Description("Source file path.")] string path,
        [Description("Namespace to remove.")] string @namespace)
    {
        return _roslynEditService.RemoveUsing(path, @namespace, null, true);
    }

    [McpServerTool]
    [Description("Remove one C# symbol from the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult RemoveSymbol(
        [Description("Source file path.")] string path,
        [Description("Roslyn selector JSON.")] string symbolSelectorJson)
    {
        return _roslynEditService.RemoveSymbol(path, symbolSelectorJson, null, true);
    }

    // --- Session Status ---

    [McpServerTool]
    [Description("Return edit workflow status for one watched source file.")]
    public EditSessionStatus GetEditStatus(
        [Description("Source file path.")] string sourceFilePath)
    {
        return _editService.GetStatus(sourceFilePath);
    }

    [McpServerTool]
    [Description("Return the CodeHands workflow tool manifest with edit priorities.")]
    public string GetWorkflowToolManifest()
    {
        var composer = new EditSessionGuidanceComposer();
        return composer.ComposeSessionBootstrapCapabilityManifest();
    }
}

// =============================================================================
// NEW RESULT TYPES (add to existing result types file or at bottom)
// =============================================================================

public record FileCapabilitiesResult(
    string FilePath,
    string Kind,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> Warnings);

public record FileMatchResult(
    string FileName,
    string FullPath,
    string RelativePath);
