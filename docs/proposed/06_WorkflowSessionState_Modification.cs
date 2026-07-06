// =============================================================================
// WorkflowSessionState.cs - MODIFICATIONS
// Part of: CodeHands_Implementation_Proposal.md
// Location: docs/proposed/
// Target: CodexAppServerBlazor/Services/Workflow/WorkflowSessionState.cs
// Description: Add workflow extension properties to existing session state
// References: 01_EditSessionModels.cs
// =============================================================================

// REPLACE the entire WorkflowSessionState class with this extended version:
// (The existing properties are preserved, new properties are added at the bottom)

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkflowSessionState
{
    // === EXISTING PROPERTIES (preserve these) ===
    public string WorkspaceRoot { get; }
    public WorkflowTurnMode InitialMode { get; }
    public DateTimeOffset StartedAt { get; }
    public bool HasAttachedSessionBootstrap { get; set; }
    public bool HasAttachedWorkspaceContext { get; set; }

    // === WORKFLOW EXTENSION PROPERTIES (add these) ===
    
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
    
    // Constructor (preserve as-is)
    public WorkflowSessionState(string workspaceRoot, WorkflowTurnMode initialMode)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        InitialMode = initialMode;
        StartedAt = DateTimeOffset.UtcNow;
    }
}

// ADD these new types at the bottom of the file (or in separate models file):
// - EditSession (from 01_EditSessionModels.cs)
// - EditSessionFile (from 01_EditSessionModels.cs)
// - MergeReviewState (from 01_EditSessionModels.cs)
// - WorkflowPhase enum (from 01_EditSessionModels.cs)
// - OverlayBuildState enum (from 01_EditSessionModels.cs)
