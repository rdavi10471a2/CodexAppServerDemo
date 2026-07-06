# CodeHands Implementation Proposal

## Overview

This document describes the implementation plan for the CodeHands workflow system, which extends the existing `WorkflowSessionState` to support a governed edit session workflow with:

- Single edit session (multiple files)
- Semantic MCP tools replacing Codex native `grep`/`ApplyPatch`
- Overlay build quality gate
- Human-in-the-loop merge with rejection handling
- Task state persistence across workflow phases
- File capability detection (including hybrid Razor files)

---

## Table of Contents

1. [Architecture Summary](#architecture-summary)
2. [New Source Files](#new-source-files)
3. [Modified Source Files](#modified-source-files)
4. [Workflow Phases](#workflow-phases)
5. [MCP Tool Mapping](#mcp-tool-mapping)
6. [Session State Extensions](#session-state-extensions)
7. [File Capability System](#file-capability-system)
8. [Implementation Details](#implementation-details)

---

## Reference MCP Server Implementation

The complete MCP server implementation that this proposal builds upon is located at:

```
docs/reference_mcp_server/
├── AICodingServicesTools.cs                    # Tool class definition
├── AICodingServicesTools.Common.cs              # Common utilities
├── AICodingServicesTools.FileContext.cs        # File operations
├── AICodingServicesTools.Indexing.cs            # Semantic search (index-based)
├── AICodingServicesTools.Roslyn.cs             # Semantic edit (Roslyn-based)
├── AICodingServicesTools.StatusAndSession.cs    # Session management
├── AICodingServicesTools.Workflow.cs            # Workflow operations
├── AICodingServicesToolResults.cs               # Result types
└── AICodingServicesMcpRuntimeState.cs          # Runtime state
```

Key capabilities in the reference implementation:

- **Semantic Search** (Indexing tools): `find_indexed_symbols`, `find_indexed_references`, `find_indexed_callers`, `query_solution_index`
- **Semantic Edit** (Roslyn tools): `submit_symbol`, `add_field`, `add_property`, `add_method`, `add_constructor`, `remove_symbol`, `add_using`, `remove_using`
- **Text Edit** (Workflow tools): `submit_file`, `replace_text_in_file`, `replace_span_in_file`, `find_text_span`
- **File Management**: `refresh_file`, `new_file`, `get_file`, `find_file`, `check_file_hash`
- **Workflow**: `start_monitor_session`, `stage_candidate_for_review`, `record_diff_decision`, `launch_staged_diff`

---

## Architecture Summary

### Current State

```
CodexConnectionService
    └── WorkflowSessionState (minimal)
    └── WorkflowTurnContextComposer
    └── TaskWorkflowContextService
    └── WorkspaceWorkflowContextService
```

### Target State

```
CodexConnectionService
    └── WorkflowSessionState (extended)
    │       ├── CurrentEditSession
    │       ├── CurrentPhase
    │       ├── MergeReviewState
    │       └── ToolUsageTracker
    ├── WorkflowTurnContextComposer
    │       └── ComposeEditSessionGuidance()
    ├── TaskWorkflowContextService
    ├── WorkspaceWorkflowContextService
    ├── FileCapabilitiesAnalyzer (NEW)
    ├── WorkflowStateManager (NEW)
    └── EditSessionBridge (NEW)
```

---

## New Source Files

### 1. `CodexAppServerBlazor/Services/Workflow/FileCapabilitiesAnalyzer.cs`

```csharp
using System.Text.RegularExpressions;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class FileCapabilitiesAnalyzer
{
    private static readonly Dictionary<string, IReadOnlyList<string>> ExtensionCapabilities = new()
    {
        { ".cs", ["submit_symbol", "add_field", "add_property", "add_method", "add_constructor", "add_nested_type", "remove_symbol", "add_using", "remove_using", "replace_text_in_file", "submit_file"] },
        { ".csproj", ["replace_text_in_file", "find_file", "submit_file"] },
        { ".sln", ["replace_text_in_file", "find_file", "submit_file"] },
        { ".slnx", ["replace_text_in_file", "find_file", "submit_file"] },
    };

    private readonly Dictionary<string, FileCapabilities> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FileCapabilities GetCapabilities(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        
        if (_cache.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        FileCapabilities capabilities = Analyze(fullPath);
        _cache[fullPath] = capabilities;
        return capabilities;
    }

    private FileCapabilities Analyze(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        // Pure code files - full toolset
        if (ExtensionCapabilities.TryGetValue(extension, out var codeTools))
        {
            return new FileCapabilities(filePath, FileKind.PureCode, codeTools, []);
        }
        
        // Hybrid Razor detection
        if (extension == ".razor" && File.Exists(filePath))
        {
            string text = File.ReadAllText(filePath);
            if (IsHybridRazor(text))
            {
                return new FileCapabilities(
                    filePath, 
                    FileKind.HybridRazor, 
                    ["replace_text_in_file", "submit_file"],
                    ["File contains both markup and @code sections. Use replace_text_in_file carefully."]);
            }
        }
        
        // Default: markup only
        return new FileCapabilities(
            filePath, 
            FileKind.Markup, 
            ["replace_text_in_file", "submit_file"],
            []);
    }

    public static bool IsHybridRazor(string text)
    {
        return text.Contains("@code", StringComparison.Ordinal)
            || text.Contains("@page", StringComparison.Ordinal)
            || text.Contains("@using", StringComparison.Ordinal)
            || text.Contains("@inherits", StringComparison.Ordinal)
            || text.Contains("@inject", StringComparison.Ordinal)
            || text.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Any(line => line.TrimStart().StartsWith("<", StringComparison.Ordinal)
                    && !line.StartsWith("///", StringComparison.Ordinal)
                    && !line.StartsWith("<!--", StringComparison.Ordinal));
    }

    public void ClearCache() => _cache.Clear();
}

public sealed record FileCapabilities(
    string FilePath,
    FileKind Kind,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> Warnings);

public enum FileKind
{
    PureCode,         // .cs - full Roslyn semantic tools
    SolutionFile,     // .sln, .csproj - text + find
    HybridRazor,      // .razor with @code - text only, with warnings
    Markup            // Everything else - text only
}
```

### 2. `CodexAppServerBlazor/Services/Workflow/WorkflowStateManager.cs`

```csharp
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;

namespace CodexAppServerBlazor.Services.Workflow;

public interface IWorkflowStateManager
{
    WorkflowSessionState GetOrCreateSession(string workspaceRoot, WorkflowTurnMode mode);
    void StartEditSession(WorkflowSessionState session, IReadOnlyList<string> filePaths, FileCapabilitiesAnalyzer analyzer);
    void CompleteEditSession(WorkflowSessionState session);
    void AdvancePhase(WorkflowSessionState session, WorkflowPhase newPhase);
    void MarkFileEdited(WorkflowSessionState session, string filePath);
    void MarkFileMerged(WorkflowSessionState session, string filePath);
    void RejectFile(WorkflowSessionState session, string filePath, string reason);
    void RecordToolUsage(WorkflowSessionState session, string toolName);
    void ResetSession(WorkflowSessionState session);
    EditSessionSummary GetEditSessionSummary(WorkflowSessionState session);
}

public sealed class WorkflowStateManager : IWorkflowStateManager
{
    private readonly CodingServicesSettingsProvider _settingsProvider;

    public WorkflowStateManager(CodingServicesSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public WorkflowSessionState GetOrCreateSession(string workspaceRoot, WorkflowTurnMode mode)
    {
        // This would be called from CodexConnectionService
        // Returns existing or new WorkflowSessionState
        throw new NotImplementedException("Integration with CodexConnectionService required");
    }

    public void StartEditSession(WorkflowSessionState session, IReadOnlyList<string> filePaths, FileCapabilitiesAnalyzer analyzer)
    {
        var files = filePaths.Select(path =>
        {
            var caps = analyzer.GetCapabilities(path);
            return new EditSessionFile
            {
                RelativePath = path,
                FileKind = caps.Kind,
                AllowedTools = caps.AllowedTools,
                Warnings = caps.Warnings,
                WorkingFilePath = GetWorkingFilePath(path)
            };
        }).ToList();

        session.CurrentEditSession = new EditSession
        {
            SessionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            Files = files
        };

        session.CurrentPhase = WorkflowPhase.Editing;
        session.HasPresentedEditGuidance = false;
    }

    public void CompleteEditSession(WorkflowSessionState session)
    {
        if (session.CurrentEditSession == null)
        {
            throw new InvalidOperationException("No active edit session.");
        }

        session.CurrentPhase = WorkflowPhase.OverlayBuild;
    }

    public void AdvancePhase(WorkflowSessionState session, WorkflowPhase newPhase)
    {
        if (!WorkflowPhaseTransitions.CanTransition(session.CurrentPhase, newPhase))
        {
            throw new InvalidOperationException(
                $"Cannot transition from {session.CurrentPhase} to {newPhase}.");
        }

        session.CurrentPhase = newPhase;
    }

    public void MarkFileEdited(WorkflowSessionState session, string filePath)
    {
        var file = GetSessionFile(session, filePath);
        file.IsEdited = true;
    }

    public void MarkFileMerged(WorkflowSessionState session, string filePath)
    {
        var file = GetSessionFile(session, filePath);
        file.IsMerged = true;
        
        CheckMergeComplete(session);
    }

    public void RejectFile(WorkflowSessionState session, string filePath, string reason)
    {
        var file = GetSessionFile(session, filePath);
        file.IsRejected = true;
        file.RejectionReason = reason;
        session.RejectedFiles.Add(filePath);
        
        CheckMergeComplete(session);
    }

    public void RecordToolUsage(WorkflowSessionState session, string toolName)
    {
        if (!session.ToolUsageCounts.TryGetValue(toolName, out var count))
        {
            count = 0;
        }
        session.ToolUsageCounts[toolName] = count + 1;
    }

    public void ResetSession(WorkflowSessionState session)
    {
        session.CurrentEditSession = null;
        session.CurrentPhase = WorkflowPhase.Idle;
        session.MergeReviewState = new MergeReviewState();
        session.RejectedFiles.Clear();
        session.LastOverlayBuild = OverlayBuildState.None;
        session.ToolUsageCounts.Clear();
    }

    public EditSessionSummary GetEditSessionSummary(WorkflowSessionState session)
    {
        var editSession = session.CurrentEditSession;
        if (editSession == null)
        {
            return new EditSessionSummary(null, 0, 0, 0, 0, [], []);
        }

        return new EditSessionSummary(
            editSession.SessionId,
            editSession.Files.Count,
            editSession.Files.Count(f => f.IsEdited),
            editSession.Files.Count(f => f.IsMerged),
            editSession.Files.Count(f => f.IsRejected),
            editSession.Files.Where(f => f.IsRejected).Select(f => new RejectedFile(f.RelativePath, f.RejectionReason ?? "")),
            session.CurrentPhase);
    }

    private void CheckMergeComplete(WorkflowSessionState session)
    {
        if (session.CurrentEditSession == null) return;

        var allDone = session.CurrentEditSession.Files.All(f => f.IsMerged || f.IsRejected);
        if (allDone)
        {
            session.CurrentPhase = session.RejectedFiles.Count > 0 
                ? WorkflowPhase.DiscussionPending 
                : WorkflowPhase.MergeComplete;
        }
    }

    private EditSessionFile GetSessionFile(WorkflowSessionState session, string filePath)
    {
        var file = session.CurrentEditSession?.Files
            .FirstOrDefault(f => f.RelativePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        
        if (file == null)
        {
            throw new InvalidOperationException($"File not in edit session: {filePath}");
        }
        
        return file;
    }

    private string GetWorkingFilePath(string relativePath)
    {
        // This would use WorkflowEditPaths from AICodingServices
        throw new NotImplementedException("Integration with WorkflowEditPaths required");
    }
}

public static class WorkflowPhaseTransitions
{
    public static bool CanTransition(WorkflowPhase from, WorkflowPhase to) => (from, to) switch
    {
        (WorkflowPhase.Idle, WorkflowPhase.Discovery) => true,
        (WorkflowPhase.Discovery, WorkflowPhase.Editing) => true,
        (WorkflowPhase.Editing, WorkflowPhase.OverlayBuild) => true,
        (WorkflowPhase.OverlayBuild, WorkflowPhase.Editing) => true,      // Build failed
        (WorkflowPhase.OverlayBuild, WorkflowPhase.MergePending) => true, // Build succeeded
        (WorkflowPhase.MergePending, WorkflowPhase.MergeComplete) => true, // All accepted
        (WorkflowPhase.MergePending, WorkflowPhase.DiscussionPending) => true, // Has rejections
        (WorkflowPhase.DiscussionPending, WorkflowPhase.Editing) => true, // Re-discuss rejected
        (WorkflowPhase.DiscussionPending, WorkflowPhase.Discovery) => true, // New proposal
        (WorkflowPhase.MergeComplete, WorkflowPhase.Rebooting) => true,
        (WorkflowPhase.Rebooting, WorkflowPhase.Idle) => true,
        (WorkflowPhase.MergeComplete, WorkflowPhase.SelfEditWarning) => true,
        _ => false
    };
}

public sealed record EditSessionSummary(
    string? SessionId,
    int TotalFiles,
    int EditedFiles,
    int MergedFiles,
    int RejectedFiles,
    IReadOnlyList<RejectedFile> Rejections,
    WorkflowPhase CurrentPhase);

public sealed record RejectedFile(string RelativePath, string Reason);
```

### 3. `CodexAppServerBlazor/Services/Workflow/EditSessionModels.cs`

```csharp
using System.Text.Json.Serialization;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class EditSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<EditSessionFile> Files { get; init; } = [];
    public string? BuildOutput { get; set; }
    public bool BuildSucceeded { get; set; }
}

public sealed class EditSessionFile
{
    public string RelativePath { get; init; } = string.Empty;
    public FileKind FileKind { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    
    // State tracking
    public bool IsEdited { get; set; }
    public bool IsMerged { get; set; }
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }
    public string? WorkingFilePath { get; set; }
}

public sealed class MergeReviewState
{
    public IReadOnlyList<EditSessionFile>? Files { get; set; }
    
    public int TotalCount => Files?.Count ?? 0;
    public int MergedCount => Files?.Count(f => f.IsMerged) ?? 0;
    public int RejectedCount => Files?.Count(f => f.IsRejected) ?? 0;
    public int PendingCount => TotalCount - MergedCount - RejectedCount;
    
    public bool IsComplete => PendingCount == 0;
    public bool HasRejections => RejectedCount > 0;
}

public enum WorkflowPhase
{
    Idle,                    // No active session
    Discovery,               // Agent analyzing/proposing
    Editing,                 // Agent making edits via MCP
    OverlayBuild,            // Building overlay (quality gate)
    MergePending,            // Awaiting human merge review
    MergeComplete,           // All files accepted
    DiscussionPending,       // Has rejected files, needs discussion
    Rebooting,               // Stopping/restarting exe
    SelfEditWarning          // Self-editing detected, needs VS restart
}

public enum OverlayBuildState
{
    None,
    Building,
    Succeeded,
    Failed
}
```

### 4. `CodexAppServerBlazor/Services/Workflow/EditSessionGuidanceComposer.cs`

```csharp
namespace CodexAppServerBlazor.Services.Workflow;

public sealed class EditSessionGuidanceComposer
{
    public string ComposeEditSessionGuidance(EditSession editSession)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Edit Session Started");
        builder.AppendLine($"Session ID: {editSession.SessionId}");
        builder.AppendLine($"Files: {editSession.Files.Count}");
        builder.AppendLine();
        
        // Group files by kind
        var codeFiles = editSession.Files.Where(f => f.FileKind == FileKind.PureCode).ToList();
        var razorFiles = editSession.Files.Where(f => f.FileKind == FileKind.HybridRazor).ToList();
        var markupFiles = editSession.Files.Where(f => f.FileKind == FileKind.Markup).ToList();
        var solutionFiles = editSession.Files.Where(f => f.FileKind == FileKind.SolutionFile).ToList();
        
        // Tool priority guidance
        builder.AppendLine("### Tool Priority");
        builder.AppendLine();
        
        if (codeFiles.Count > 0)
        {
            builder.AppendLine("**C# Files (.cs)**");
            builder.AppendLine("| Priority | Tool | When to Use |");
            builder.AppendLine("|----------|------|--------------|");
            builder.AppendLine("| 1 | `submit_symbol` | Replace/change existing symbols |");
            builder.AppendLine("| 2 | `add_field`, `add_property`, `add_method`, `add_constructor` | Add new members |");
            builder.AppendLine("| 3 | `add_using`, `remove_using` | Manage usings |");
            builder.AppendLine("| 4 | `replace_text_in_file` | Text-based changes |");
            builder.AppendLine("| 5 | `submit_file` | Full file (last resort) |");
            builder.AppendLine();
        }
        
        if (solutionFiles.Count > 0)
        {
            builder.AppendLine("**Solution/Project Files (.sln, .csproj)**");
            builder.AppendLine("| Priority | Tool | When to Use |");
            builder.AppendLine("|----------|------|--------------|");
            builder.AppendLine("| 1 | `replace_text_in_file` | Text-based changes |");
            builder.AppendLine("| 2 | `find_file` | Discovery only |");
            builder.AppendLine("| 3 | `submit_file` | Full file (last resort) |");
            builder.AppendLine();
        }
        
        if (razorFiles.Count > 0)
        {
            builder.AppendLine("**Hybrid Razor Files (.razor with @code)**");
            builder.AppendLine("| Priority | Tool | When to Use |");
            builder.AppendLine("|----------|------|--------------|");
            builder.AppendLine("| 1 | `replace_text_in_file` | Text-based changes (carefully) |");
            builder.AppendLine("| 2 | `submit_file` | Full file (last resort) |");
            foreach (var file in razorFiles)
            {
                if (file.Warnings.Count > 0)
                {
                    builder.AppendLine($"  - *{file.RelativePath}: {string.Join(", ", file.Warnings)}*");
                }
            }
            builder.AppendLine();
        }
        
        if (markupFiles.Count > 0)
        {
            builder.AppendLine("**Markup Files (.razor, .md, .json, etc.)**");
            builder.AppendLine("| Priority | Tool | When to Use |");
            builder.AppendLine("|----------|------|--------------|");
            builder.AppendLine("| 1 | `replace_text_in_file` | Text-based changes |");
            builder.AppendLine("| 2 | `submit_file` | Full file (last resort) |");
            builder.AppendLine();
        }
        
        // Blocked tools
        builder.AppendLine("### DO NOT USE");
        builder.AppendLine("- Codex native `ApplyPatch` - blocked");
        builder.AppendLine("- `grep` for C# symbol search - use MCP semantic tools instead");
        builder.AppendLine();
        
        // Files list
        builder.AppendLine("### Files in this session:");
        foreach (var file in editSession.Files)
        {
            var status = file.IsEdited ? "[EDITED]" : "[PENDING]";
            var kind = file.FileKind.ToString().ToLowerInvariant();
            builder.AppendLine($"- {file.RelativePath} ({kind}) {status}");
        }
        
        return builder.ToString();
    }
    
    public string ComposeSessionBootstrapCapabilityManifest()
    {
        var builder = new StringBuilder();
        builder.AppendLine("## MCP-First Workflow");
        builder.AppendLine();
        builder.AppendLine("This session uses AICodingServices MCP tools exclusively.");
        builder.AppendLine();
        
        builder.AppendLine("### Discovery (instead of grep)");
        builder.AppendLine("- Use `find_indexed_symbols` to search C# symbols");
        builder.AppendLine("- Use `find_indexed_references` to find symbol usages");
        builder.AppendLine("- Use `find_indexed_callers` to find method callers");
        builder.AppendLine("- Use `query_solution_index` for scope-based queries");
        builder.AppendLine("- Use `find_file` to locate files by pattern");
        builder.AppendLine("- Use `get_solution_index_tree` for project structure");
        builder.AppendLine();
        
        builder.AppendLine("### Edits (instead of ApplyPatch)");
        builder.AppendLine("- For C# symbol edits: use `submit_symbol`, `add_*`, `remove_symbol`");
        builder.AppendLine("- For text edits: use `replace_text_in_file`");
        builder.AppendLine("- For new files: use `new_file` then edit working copy");
        builder.AppendLine("- For existing files: use `refresh_file` first, then edit");
        builder.AppendLine();
        
        builder.AppendLine("### File Workflow");
        builder.AppendLine("- Use `get_file` to read watched source files");
        builder.AppendLine("- Edits go to monitor-owned Working copies only");
        builder.AppendLine("- Use `get_edit_status` to check file session state");
        builder.AppendLine();
        
        return builder.ToString();
    }
}
```

### 5. `CodexAppServerBlazor/Services/Workflow/EditSessionBridge.cs`

```csharp
using CodexAppServerBlazor.AICodingServices.Workflow;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class EditSessionBridge
{
    private readonly WorkflowEditService _editService;
    private readonly IWorkflowStateManager _stateManager;

    public EditSessionBridge(
        WorkflowEditService editService,
        IWorkflowStateManager stateManager)
    {
        _editService = editService;
        _stateManager = stateManager;
    }

    public void OnFileRefreshed(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileEdited(session, filePath);
    }

    public void OnEditSubmitted(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileEdited(session, filePath);
    }

    public void OnOverlayBuildStarted(WorkflowSessionState session)
    {
        session.LastOverlayBuild = OverlayBuildState.Building;
        _stateManager.AdvancePhase(session, WorkflowPhase.OverlayBuild);
    }

    public void OnOverlayBuildCompleted(WorkflowSessionState session, bool succeeded, string? output)
    {
        session.LastOverlayBuild = succeeded ? OverlayBuildState.Succeeded : OverlayBuildState.Failed;
        session.LastBuildOutput = output;
        
        if (session.CurrentEditSession != null)
        {
            session.CurrentEditSession.BuildSucceeded = succeeded;
            session.CurrentEditSession.BuildOutput = output;
        }
        
        if (succeeded)
        {
            _stateManager.AdvancePhase(session, WorkflowPhase.MergePending);
            InitializeMergeReview(session);
        }
        else
        {
            _stateManager.AdvancePhase(session, WorkflowPhase.Editing);
        }
    }

    public void OnFileMerged(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileMerged(session, filePath);
    }

    public void OnFileRejected(WorkflowSessionState session, string filePath, string reason)
    {
        _stateManager.RejectFile(session, filePath, reason);
    }

    private void InitializeMergeReview(WorkflowSessionState session)
    {
        if (session.CurrentEditSession == null) return;
        
        session.MergeReviewState = new MergeReviewState
        {
            Files = session.CurrentEditSession.Files
        };
    }
}
```

### 6. `Mcp/WorkspaceMcpTools.cs` - New Tools to Add

Add these MCP tools to the existing `WorkspaceMcpTools.cs`:

```csharp
[McpServerTool]
[Description("Analyze file capabilities for the CodeHands workflow. Returns file kind and allowed MCP tools.")]
public FileCapabilitiesResult AnalyzeFileCapabilities(
    [Description("Source file path, absolute or relative to the workspace.")] string filePath,
    [Description("Optional session handle.")] string? sessionId = null)
{
    var analyzer = new FileCapabilitiesAnalyzer();
    var caps = analyzer.GetCapabilities(filePath);
    
    return new FileCapabilitiesResult(
        filePath,
        caps.Kind.ToString(),
        caps.AllowedTools.ToList(),
        caps.Warnings.ToList());
}

[McpServerTool]
[Description("Return the current edit session state including files, phase, and merge progress.")]
public EditSessionStateResult GetEditSessionState(
    [Description("Optional session ID.")] string? sessionId = null)
{
    var session = GetCurrentSession(); // From runtime state
    if (session?.CurrentEditSession == null)
    {
        return new EditSessionStateResult(null, "idle", 0, 0, 0, 0, [], []);
    }
    
    var editSession = session.CurrentEditSession;
    return new EditSessionStateResult(
        editSession.SessionId,
        session.CurrentPhase.ToString().ToLowerInvariant(),
        editSession.Files.Count,
        editSession.Files.Count(f => f.IsEdited),
        editSession.Files.Count(f => f.IsMerged),
        editSession.Files.Count(f => f.IsRejected),
        editSession.Files.Select(f => new EditSessionFileInfo(
            f.RelativePath,
            f.FileKind.ToString(),
            f.IsEdited,
            f.IsMerged,
            f.IsRejected,
            f.RejectionReason)),
        session.RejectedFiles);
}

[McpServerTool]
[Description("Signal that editing is complete and request overlay build.")]
public OverlayBuildRequestResult RequestOverlayBuild(
    [Description("Session ID from edit session start.")] string sessionId)
{
    // This triggers the build via EditSessionBridge
    // Returns immediately with "building" status
    // Client should poll GetEditSessionState for result
    return new OverlayBuildRequestResult("building", "Overlay build started.");
}

[McpServerTool]
[Description("Get the CodeHands workflow tool manifest with edit priorities.")]
public string GetWorkflowToolManifest()
{
    var guidance = new EditSessionGuidanceComposer();
    return guidance.ComposeSessionBootstrapCapabilityManifest();
}

// New result types
public record FileCapabilitiesResult(
    string FilePath,
    string Kind,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> Warnings);

public record EditSessionStateResult(
    string? SessionId,
    string Phase,
    int TotalFiles,
    int EditedFiles,
    int MergedFiles,
    int RejectedFiles,
    IReadOnlyList<EditSessionFileInfo> Files,
    IReadOnlyList<string> RejectedFilePaths);

public record EditSessionFileInfo(
    string RelativePath,
    string Kind,
    bool IsEdited,
    bool IsMerged,
    bool IsRejected,
    string? RejectionReason);

public record OverlayBuildRequestResult(
    string Status,
    string Message);
```

---

## Modified Source Files

### 1. `CodexAppServerBlazor/Services/Workflow/WorkflowSessionState.cs`

**Changes: Add workflow extension properties**

```csharp
namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkflowSessionState
{
    // === EXISTING PROPERTIES ===
    public string WorkspaceRoot { get; }
    public WorkflowTurnMode InitialMode { get; }
    public DateTimeOffset StartedAt { get; }
    public bool HasAttachedSessionBootstrap { get; set; }
    public bool HasAttachedWorkspaceContext { get; set; }

    // === WORKFLOW EXTENSION PROPERTIES ===
    
    // Task tracking
    public string? CurrentActiveTaskId { get; set; }
    public string? CurrentTaskPromptPath { get; set; }
    
    // Edit session (single, multi-file)
    public EditSession? CurrentEditSession { get; set; }
    
    // Workflow phase
    public WorkflowPhase CurrentPhase { get; set; } = WorkflowPhase.Idle;
    
    // Merge tracking
    public MergeReviewState MergeReviewState { get; set; } = new();
    public List<string> RejectedFiles { get; } = new();
    public bool HasRejections => RejectedFiles.Count > 0;
    
    // Build state
    public OverlayBuildState LastOverlayBuild { get; set; } = OverlayBuildState.None;
    public string? LastBuildOutput { get; set; }
    
    // Tool usage tracking
    public Dictionary<string, int> ToolUsageCounts { get; } = new();
    
    // Guidance presentation
    public bool HasPresentedEditGuidance { get; set; }
    
    // Turn continuity
    public int TurnCount { get; set; }
    public DateTimeOffset? LastTurnAt { get; set; }
    
    // Constructor remains same
    public WorkflowSessionState(string workspaceRoot, WorkflowTurnMode initialMode)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        InitialMode = initialMode;
        StartedAt = DateTimeOffset.UtcNow;
    }
}
```

### 2. `CodexAppServerBlazor/Services/Workflow/WorkflowTurnContextComposer.cs`

**Changes: Add edit session guidance to composed prompts**

```csharp
using System.Text;
using CodexAppServerBlazor.Services.Tasks;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkflowTurnContextComposer : IWorkflowTurnContextComposer
{
    private readonly EditSessionGuidanceComposer _guidanceComposer = new();

    public WorkflowTurnEnvelope Compose(
        string userPrompt,
        string workspaceRoot,
        WorkflowTurnMode mode,
        WorkflowSessionState sessionState,
        SessionBootstrapPolicy sessionBootstrapPolicy,
        WorkflowPromptSection workspaceContext,
        WorkflowTurnTaskContext taskContext)
    {
        // ... existing validation ...

        StringBuilder prompt = new();
        
        // Session bootstrap (existing)
        if (includeSessionBootstrap)
        {
            prompt.AppendLine("Coding Services session bootstrap policy:");
            prompt.AppendLine(sessionBootstrapPolicy.PromptText);
            prompt.AppendLine();
            
            // Add MCP-first workflow manifest
            prompt.AppendLine(_guidanceComposer.ComposeSessionBootstrapCapabilityManifest());
        }

        // Task context (existing)
        if (mode == WorkflowTurnMode.Work)
        {
            prompt.AppendLine(taskContext.PromptMarkdown);
            prompt.AppendLine();
        }

        // Workspace context (existing)
        if (includeWorkspaceContext)
        {
            prompt.AppendLine(workspaceContext.PromptMarkdown);
            prompt.AppendLine();
        }

        // === NEW: Edit session guidance ===
        if (sessionState.CurrentEditSession != null && !sessionState.HasPresentedEditGuidance)
        {
            prompt.AppendLine(_guidanceComposer.ComposeEditSessionGuidance(sessionState.CurrentEditSession));
            prompt.AppendLine();
            sessionState.HasPresentedEditGuidance = true;
        }
        
        // === NEW: Phase-specific guidance ===
        if (sessionState.CurrentPhase != WorkflowPhase.Idle)
        {
            prompt.AppendLine(ComposePhaseGuidance(sessionState));
            prompt.AppendLine();
        }

        // Codex cwd and mode (existing)
        prompt.AppendLine("Codex cwd:");
        prompt.AppendLine(workspaceRoot);
        prompt.AppendLine();
        prompt.AppendLine("Workflow mode:");
        prompt.AppendLine("- " + mode);
        prompt.AppendLine();
        
        // User request (existing)
        prompt.AppendLine("User request:");
        prompt.AppendLine(userPrompt);

        return new WorkflowTurnEnvelope(
            prompt.ToString(),
            mode,
            includeSessionBootstrap,
            includeWorkspaceContext,
            sessionBootstrapPolicy,
            workspaceContext,
            taskContext);
    }

    private string ComposePhaseGuidance(WorkflowSessionState session)
    {
        return session.CurrentPhase switch
        {
            WorkflowPhase.Discovery => 
                "Phase: DISCOVERY - Analyze the workspace and propose which files need editing.",
            WorkflowPhase.Editing => 
                $"Phase: EDITING - Make edits using MCP tools. {session.CurrentEditSession?.Files.Count ?? 0} files in session.",
            WorkflowPhase.OverlayBuild => 
                "Phase: OVERLAY BUILD - Building to verify edits. Do not make further changes.",
            WorkflowPhase.MergePending => 
                $"Phase: MERGE PENDING - Awaiting human review. {session.MergeReviewState.MergedCount}/{session.MergeReviewState.TotalCount} files merged.",
            WorkflowPhase.DiscussionPending => 
                $"Phase: DISCUSSION NEEDED - {session.RejectedFiles.Count} file(s) rejected. Discuss with user.",
            WorkflowPhase.MergeComplete => 
                "Phase: MERGE COMPLETE - All files accepted. Ready for rebuild.",
            WorkflowPhase.Rebooting => 
                "Phase: REBOOTING - Stopping and restarting application.",
            _ => string.Empty
        };
    }
}
```

### 3. `CodexAppServerBlazor/Services/CodexConnectionService.cs`

**Changes: Integrate WorkflowStateManager and EditSessionBridge**

```csharp
// Add new dependencies to constructor
private readonly IWorkflowStateManager _workflowStateManager;
private readonly EditSessionBridge _editSessionBridge;
private readonly FileCapabilitiesAnalyzer _fileCapabilitiesAnalyzer;

public CodexConnectionService(
    WorkspaceState workspaceState,
    IWorkspaceWorkflowContextService workspaceWorkflowContextService,
    ITaskWorkflowContextService taskWorkflowContextService,
    SessionBootstrapPolicyService sessionBootstrapPolicyService,
    IWorkflowTurnContextComposer workflowTurnContextComposer,
    IWorkflowStateManager workflowStateManager,        // NEW
    FileCapabilitiesAnalyzer fileCapabilitiesAnalyzer, // NEW
    Func<CodexAppServerClient>? clientFactory = null)
{
    // ... existing assignments ...
    _workflowStateManager = workflowStateManager;
    _editSessionBridge = new EditSessionBridge(editService, workflowStateManager);
    _fileCapabilitiesAnalyzer = fileCapabilitiesAnalyzer;
}

// Add new methods
public void StartEditSession(IReadOnlyList<string> filePaths)
{
    if (workflowSessionState == null)
    {
        throw new InvalidOperationException("Start a thread first.");
    }
    
    _workflowStateManager.StartEditSession(workflowSessionState, filePaths, _fileCapabilitiesAnalyzer);
}

public void CompleteEditSession()
{
    if (workflowSessionState == null)
    {
        throw new InvalidOperationException("No active session.");
    }
    
    _workflowStateManager.CompleteEditSession(workflowSessionState);
    _editSessionBridge.OnOverlayBuildStarted(workflowSessionState);
    
    // Trigger overlay build (async)
    _ = Task.Run(() => RunOverlayBuild(workflowSessionState));
}

private async Task RunOverlayBuild(WorkflowSessionState session)
{
    try
    {
        // Use MSBuild/DotNet from AICodingServices
        var buildResult = await RunDotNetBuildAsync(session);
        bool succeeded = buildResult.ExitCode == 0;
        _editSessionBridge.OnOverlayBuildCompleted(session, succeeded, buildResult.Output);
    }
    catch (Exception ex)
    {
        _editSessionBridge.OnOverlayBuildCompleted(session, false, ex.Message);
    }
}

public void MarkFileMerged(string filePath)
{
    if (workflowSessionState == null) return;
    _editSessionBridge.OnFileMerged(workflowSessionState, filePath);
}

public void RejectFile(string filePath, string reason)
{
    if (workflowSessionState == null) return;
    _editSessionBridge.OnFileRejected(workflowSessionState, filePath, reason);
}
```

### 4. `CodexAppServerBlazor/docs/policy/CS-SessionBootstrap.txt`

**Changes: Add MCP-first workflow guidance**

```markdown
Coding Services host bootstrap policy:

- This session is hosted by Coding Services.
- When Coding Services host policy, selected-workspace policy, or attached workspace tooling conflicts with ambient global preferences, unrelated global skills, or prior-project workflow habits, the Coding Services host policy takes precedence for this session.
- Treat the selected CWD as the authoritative target workspace for this thread.
- Workspace-local AGENTS.md rules apply only when present in the selected CWD or its trusted project root.
- Do not assume the Coding Services host repository is the target workspace unless the selected CWD matches it.
- Do not import terminology, workflows, or tool preferences from unrelated globally available skills unless this session explicitly activates them.
- When the workspace or host provides a preferred path for context gathering, editing, review, indexing, or task handling, use that path instead of generic global defaults.

## MCP-First Workflow

This session uses AICodingServices MCP tools exclusively. Codex native tools are restricted.

### Discovery (instead of grep)

Use these MCP tools for workspace exploration:

- `get_watched_solution_digest` - High-level project structure
- `get_watched_solution_summary` - Detailed project summary  
- `get_solution_index_tree` - Indexed file tree
- `find_indexed_symbols` - Search C# symbols by name
- `find_indexed_references` - Find symbol usages
- `find_indexed_callers` - Find method callers
- `query_solution_index` - Scope-based index queries
- `find_file` - Locate files by pattern

### Edits (instead of ApplyPatch)

**Never use Codex native `ApplyPatch`. It is blocked.**

For C# symbol edits:
- `submit_symbol` - Replace/change existing symbols
- `add_field`, `add_property`, `add_method`, `add_constructor` - Add members
- `add_using`, `remove_using` - Manage usings
- `remove_symbol` - Remove symbols

For text-based edits:
- `replace_text_in_file` - Replace exact text
- `submit_file` - Full file replacement (last resort)

For file management:
- `refresh_file` - Refresh existing file into working copy
- `new_file` - Create working copy for new file
- `get_edit_status` - Check file session state

### Edit Session Workflow

1. Start a thread and select a workspace
2. For Work mode: an active task is required
3. In Discovery phase: analyze and propose files to edit
4. When edit session starts: use tool priority guidance
5. Make edits only in monitor-owned Working copies
6. When editing complete: request overlay build
7. If build succeeds: proceed to human merge
8. If build fails: fix issues and rebuild

### Tool Priority by File Type

**C# Files (.cs)**
1. Roslyn semantic tools (`submit_symbol`, `add_*`, `remove_*`)
2. `replace_text_in_file` for text changes
3. `submit_file` for full replacement (last resort)

**Solution/Project Files (.sln, .csproj)**
1. `replace_text_in_file` for text changes
2. `find_file` for discovery
3. `submit_file` for full replacement (last resort)

**Razor Files (.razor with @code sections)**
1. `replace_text_in_file` carefully
2. `submit_file` for full replacement (last resort)

**Markup Files (.md, .json, .txt, etc.)**
1. `replace_text_in_file`
2. `submit_file` for full replacement (last resort)

### Phase Guidance

- **DISCOVERY**: Analyze workspace, propose files
- **EDITING**: Make edits via MCP tools
- **OVERLAY BUILD**: Wait for build verification
- **MERGE PENDING**: Human reviews and merges files
- **DISCUSSION NEEDED**: File(s) rejected, discuss with user
- **MERGE COMPLETE**: All accepted, ready for rebuild

### File Capability Analysis

When starting an edit session, use `analyze_file_capabilities` to understand:
- What tools are allowed for each file
- Whether a .razor file has @code sections
- Any warnings for hybrid files

### Freshness Rules

- Treat indexed summaries as stale after source edits
- Use `get_watched_solution_digest` as freshness gate
- Refresh MCP summaries after compile/reindex

### Task Memory

- Keep durable workflow memory in task artifacts
- Update task notes at end of each turn
- Solution index is volatile, not durable memory
```

---

## Workflow Phases

```
┌─────────────────────────────────────────────────────────────────────┐
│  TURN START                                                          │
│  User submits prompt → Agent receives with task context               │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: DISCOVERY                                                     │
│  Agent analyzes workspace, proposes edit session                       │
│  - Lists files to edit                                                │
│  - Server responds with file capabilities per file                    │
│  - Agent calls start_edit_session MCP tool                           │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: EDITING                                                      │
│  Agent edits files via MCP tools                                      │
│  - Uses semantic tools for .cs files                                 │
│  - Uses text tools for markup/solution files                         │
│  - Edits go to working/overlay copies                               │
│  - Agent calls signal_editing_complete                              │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: OVERLAY BUILD                                                │
│  Server runs build against overlay                                    │
│  - If FAIL → Agent notified, returns to Editing                       │
│  - If SUCCESS → Proceed to Merge                                     │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: MERGE PENDING                                                │
│  Human reviews each file via Blazor merge page                        │
│  - ACCEPT → File merged to actual                                    │
│  - REJECT → File flagged with reason                                 │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  REJECTION HANDLING (if any)                                         │
│  For each rejected file:                                             │
│  1. Discussion between agent/user about why                          │
│  2. New turn starts for that file                                   │
│  3. Returns to EDITING phase for that file                          │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: MERGE COMPLETE                                               │
│  All files accepted                                                   │
│  - Build new exe                                                     │
│  - Stop current exe                                                  │
│  - Start new exe                                                     │
│  - Note: Self-editing → warn user, restart from VS                  │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: QA                                                           │
│  User tests the changes                                               │
│  - Next prompt starts new turn cycle                                 │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  TURN END                                                            │
│  Agent updates task md file                                           │
└─────────────────────────────────────────────────────────────────────┘
```

---

## MCP Tool Mapping

### Discovery Tools (Replace grep)

| Need | MCP Tool | Returns |
|------|----------|---------|
| Find symbols | `find_indexed_symbols` | C# symbols with locations |
| Find references | `find_indexed_references` | Where symbol is used |
| Find callers | `find_indexed_callers` | Who calls a method |
| Project structure | `get_solution_index_tree` | File/folder tree |
| Scope queries | `query_solution_index` | Files by namespace/folder |
| Locate files | `find_file` | Files by pattern |

### Edit Tools (Replace ApplyPatch)

| Need | MCP Tool | For |
|------|---------|-----|
| Replace symbol | `submit_symbol` | .cs - symbol changes |
| Add members | `add_field/property/method/constructor` | .cs - new members |
| Remove symbol | `remove_symbol` | .cs - delete |
| Manage usings | `add_using` / `remove_using` | .cs - directives |
| Replace text | `replace_text_in_file` | All files - text |
| Full file | `submit_file` | All files - last resort |
| Refresh file | `refresh_file` | Existing files |
| New file | `new_file` | New files |

### Workflow Tools (NEW)

| Tool | Purpose |
|------|---------|
| `analyze_file_capabilities` | Get file kind and allowed tools |
| `get_edit_session_state` | Current edit session status |
| `request_overlay_build` | Trigger overlay build |
| `get_workflow_tool_manifest` | Get tool priority manifest |

---

## Session State Extensions

### WorkflowSessionState Full Definition

```csharp
public sealed class WorkflowSessionState
{
    // === EXISTING ===
    public string WorkspaceRoot { get; }
    public WorkflowTurnMode InitialMode { get; }
    public DateTimeOffset StartedAt { get; }
    public bool HasAttachedSessionBootstrap { get; set; }
    public bool HasAttachedWorkspaceContext { get; set; }

    // === TASK TRACKING ===
    public string? CurrentActiveTaskId { get; set; }
    public string? CurrentTaskPromptPath { get; set; }

    // === EDIT SESSION ===
    public EditSession? CurrentEditSession { get; set; }

    // === WORKFLOW PHASE ===
    public WorkflowPhase CurrentPhase { get; set; } = WorkflowPhase.Idle;

    // === MERGE TRACKING ===
    public MergeReviewState MergeReviewState { get; set; } = new();
    public List<string> RejectedFiles { get; } = new();
    public bool HasRejections => RejectedFiles.Count > 0;

    // === BUILD STATE ===
    public OverlayBuildState LastOverlayBuild { get; set; } = OverlayBuildState.None;
    public string? LastBuildOutput { get; set; }

    // === TOOL USAGE ===
    public Dictionary<string, int> ToolUsageCounts { get; } = new();

    // === GUIDANCE ===
    public bool HasPresentedEditGuidance { get; set; }

    // === TURN CONTINUITY ===
    public int TurnCount { get; set; }
    public DateTimeOffset? LastTurnAt { get; set; }

    public WorkflowSessionState(string workspaceRoot, WorkflowTurnMode initialMode)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        InitialMode = initialMode;
        StartedAt = DateTimeOffset.UtcNow;
    }
}
```

### Supporting Types

```csharp
public sealed class EditSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<EditSessionFile> Files { get; init; } = [];
    public string? BuildOutput { get; set; }
    public bool BuildSucceeded { get; set; }
}

public sealed class EditSessionFile
{
    public string RelativePath { get; init; } = string.Empty;
    public FileKind FileKind { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    
    public bool IsEdited { get; set; }
    public bool IsMerged { get; set; }
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }
    public string? WorkingFilePath { get; set; }
}

public sealed class MergeReviewState
{
    public IReadOnlyList<EditSessionFile>? Files { get; set; }
    public int TotalCount => Files?.Count ?? 0;
    public int MergedCount => Files?.Count(f => f.IsMerged) ?? 0;
    public int RejectedCount => Files?.Count(f => f.IsRejected) ?? 0;
    public int PendingCount => TotalCount - MergedCount - RejectedCount;
    public bool IsComplete => PendingCount == 0;
    public bool HasRejections => RejectedCount > 0;
}

public enum WorkflowPhase
{
    Idle,
    Discovery,
    Editing,
    OverlayBuild,
    MergePending,
    MergeComplete,
    DiscussionPending,
    Rebooting,
    SelfEditWarning
}

public enum OverlayBuildState
{
    None,
    Building,
    Succeeded,
    Failed
}

public enum FileKind
{
    PureCode,
    SolutionFile,
    HybridRazor,
    Markup
}
```

---

## File Capability System

### Detection Logic

```csharp
public FileCapabilities Analyze(string filePath)
{
    string extension = Path.GetExtension(filePath).ToLowerInvariant();
    
    // Pure code
    if (extension == ".cs")
        return (FileKind.PureCode, semanticTools);
    
    // Solution files
    if (extension == ".csproj" || extension == ".sln" || extension == ".slnx")
        return (FileKind.SolutionFile, textPlusFindTools);
    
    // Hybrid Razor
    if (extension == ".razor" && IsHybridRazor(filePath))
        return (FileKind.HybridRazor, textOnlyWithWarnings);
    
    // Default markup
    return (FileKind.Markup, textOnlyTools);
}

public static bool IsHybridRazor(string text)
{
    return text.Contains("@code")
        || text.Contains("@page")
        || text.Contains("@using")
        || text.Contains("@inherits")
        || text.Contains("@inject")
        || ContainsMarkupLine(text);
}

private static bool ContainsMarkupLine(string text)
{
    return text.Split(['\r\n', '\n'])
        .Any(line => line.TrimStart().StartsWith("<")
            && !line.StartsWith("///")
            && !line.StartsWith("<!--"));
}
```

### Tool Sets

| FileKind | Tools |
|----------|-------|
| PureCode | submit_symbol, add_field, add_property, add_method, add_constructor, add_nested_type, remove_symbol, add_using, remove_using, replace_text_in_file, submit_file |
| SolutionFile | replace_text_in_file, find_file, submit_file |
| HybridRazor | replace_text_in_file, submit_file (+ warnings) |
| Markup | replace_text_in_file, submit_file |

---

## Implementation Details

### Phase Transitions

```csharp
public static class WorkflowPhaseTransitions
{
    public static bool CanTransition(WorkflowPhase from, WorkflowPhase to) => (from, to) switch
    {
        (Idle, Discovery) => true,
        (Discovery, Editing) => true,
        (Editing, OverlayBuild) => true,
        (OverlayBuild, Editing) => true,          // Build failed
        (OverlayBuild, MergePending) => true,     // Build succeeded
        (MergePending, MergeComplete) => true,    // All accepted
        (MergePending, DiscussionPending) => true, // Has rejections
        (DiscussionPending, Editing) => true,     // Re-discuss
        (DiscussionPending, Discovery) => true,   // New proposal
        (MergeComplete, Rebooting) => true,
        (Rebooting, Idle) => true,
        (MergeComplete, SelfEditWarning) => true,
        _ => false
    };
}
```

### Self-Edit Detection

```csharp
public static class SelfEditDetector
{
    private static readonly string[] SelfEditPatterns =
    {
        ".dll",
        ".exe",
        "appsettings",
        // Add more patterns as discovered
    };

    public static bool WouldRequireRestart(EditSession editSession)
    {
        return editSession.Files.Any(f =>
            SelfEditPatterns.Any(pattern => 
                f.RelativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    public static WorkflowPhase GetPostMergePhase(EditSession editSession)
    {
        return WouldRequireRestart(editSession)
            ? WorkflowPhase.SelfEditWarning
            : WorkflowPhase.Rebooting;
    }
}
```

### DI Registration

In `CodexAppServerBlazor/Program.cs`:

```csharp
// Services
builder.Services.AddSingleton<FileCapabilitiesAnalyzer>();
builder.Services.AddSingleton<IWorkflowStateManager, WorkflowStateManager>();

// MCP Tools (in McpHostFactory)
services.AddSingleton<EditSessionGuidanceComposer>();
```

---

## Open Items

| Item | Status | Notes |
|------|--------|-------|
| Self-edit detection | Proposed | Needs runtime verification |
| VS restart for self-edit | To work out | Depends on development setup |
| Task state machine | Proposed | InProgress → Editing → Merging → Done |
| Blazor merge page | Not implemented | Future work |
| Feature completion | To determine | How to close a feature |

---

## Files Summary

### Reference Implementation (Already Exists)

| Folder | Purpose |
|--------|---------|
| `docs/reference_mcp_server/` | Complete MCP server implementation to integrate |

### New Files to Create

| File | Purpose |
|------|---------|
| `Services/Workflow/FileCapabilitiesAnalyzer.cs` | Detect file kind and allowed tools |
| `Services/Workflow/WorkflowStateManager.cs` | Manage workflow state transitions |
| `Services/Workflow/EditSessionModels.cs` | Data models for edit sessions |
| `Services/Workflow/EditSessionGuidanceComposer.cs` | Generate tool priority guidance |
| `Services/Workflow/EditSessionBridge.cs` | Bridge MCP to state manager |
| `Mcp/WorkspaceMcpTools.cs` additions | New workflow MCP tools |

### Modified Files

| File | Changes |
|------|---------|
| `Services/Workflow/WorkflowSessionState.cs` | Add extension properties |
| `Services/Workflow/WorkflowTurnContextComposer.cs` | Add edit session guidance to prompts |
| `Services/CodexConnectionService.cs` | Integrate state manager and bridge |
| `docs/policy/CS-SessionBootstrap.txt` | Add MCP-first workflow policy |
