// =============================================================================
// EditSessionModels.cs
// Part of: CodeHands_Implementation_Proposal.md
// Location: docs/proposed/
// Description: Data models for edit sessions, merge tracking, and workflow phases
// =============================================================================

namespace CodexAppServerBlazor.Services.Workflow;

/// <summary>
/// Represents a single edit session containing multiple files being edited together.
/// </summary>
public sealed class EditSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<EditSessionFile> Files { get; init; } = [];
    public string? BuildOutput { get; set; }
    public bool BuildSucceeded { get; set; }
}

/// <summary>
/// Represents a single file within an edit session.
/// </summary>
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

/// <summary>
/// Tracks merge review progress across all files in an edit session.
/// </summary>
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

/// <summary>
/// Workflow phases for the CodeHands edit session lifecycle.
/// </summary>
public enum WorkflowPhase
{
    /// <summary>No active session</summary>
    Idle,
    
    /// <summary>Agent analyzing workspace and proposing files</summary>
    Discovery,
    
    /// <summary>Agent making edits via MCP tools</summary>
    Editing,
    
    /// <summary>Building overlay to verify edits (quality gate)</summary>
    OverlayBuild,
    
    /// <summary>Awaiting human merge review</summary>
    MergePending,
    
    /// <summary>All files accepted</summary>
    MergeComplete,
    
    /// <summary>Has rejected files, needs discussion</summary>
    DiscussionPending,
    
    /// <summary>Stopping/restarting exe</summary>
    Rebooting,
    
    /// <summary>Self-editing detected, needs VS restart</summary>
    SelfEditWarning
}

/// <summary>
/// Overlay build states.
/// </summary>
public enum OverlayBuildState
{
    None,
    Building,
    Succeeded,
    Failed
}

/// <summary>
/// File kinds for capability analysis.
/// </summary>
public enum FileKind
{
    /// <summary>.cs files - full Roslyn semantic tools</summary>
    PureCode,
    
    /// <summary>.sln, .csproj - text editing + find</summary>
    SolutionFile,
    
    /// <summary>.razor with @code sections - text only with warnings</summary>
    HybridRazor,
    
    /// <summary>.razor markup, .md, .json, .txt, etc.</summary>
    Markup
}
