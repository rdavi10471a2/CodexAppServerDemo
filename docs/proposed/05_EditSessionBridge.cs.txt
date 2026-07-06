// =============================================================================
// EditSessionBridge.cs
// Part of: CodeHands_Implementation_Proposal.md
// Location: docs/proposed/
// Description: Bridges MCP tool events to workflow state manager
// References: 01_EditSessionModels.cs, 03_WorkflowStateManager.cs
// =============================================================================

namespace CodexAppServerBlazor.Services.Workflow;

/// <summary>
/// Bridges MCP tool events to the workflow state manager.
/// Handles event flow from MCP tools through to state transitions.
/// </summary>
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

    /// <summary>
    /// Called when a file is refreshed into the working copy.
    /// </summary>
    public void OnFileRefreshed(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileEdited(session, filePath);
    }

    /// <summary>
    /// Called when an edit is submitted to the working copy.
    /// </summary>
    public void OnEditSubmitted(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileEdited(session, filePath);
    }

    /// <summary>
    /// Called when overlay build starts.
    /// </summary>
    public void OnOverlayBuildStarted(WorkflowSessionState session)
    {
        session.LastOverlayBuild = OverlayBuildState.Building;
        _stateManager.AdvancePhase(session, WorkflowPhase.OverlayBuild);
    }

    /// <summary>
    /// Called when overlay build completes.
    /// </summary>
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

    /// <summary>
    /// Called when a file is accepted by human.
    /// </summary>
    public void OnFileMerged(WorkflowSessionState session, string filePath)
    {
        _stateManager.MarkFileMerged(session, filePath);
    }

    /// <summary>
    /// Called when a file is rejected by human.
    /// </summary>
    public void OnFileRejected(WorkflowSessionState session, string filePath, string reason)
    {
        _stateManager.RejectFile(session, filePath, reason);
    }

    /// <summary>
    /// Records tool usage for diagnostics.
    /// </summary>
    public void OnToolUsed(WorkflowSessionState session, string toolName)
    {
        _stateManager.RecordToolUsage(session, toolName);
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

/// <summary>
/// Detects if an edit session would require application restart.
/// </summary>
public static class SelfEditDetector
{
    private static readonly string[] SelfEditPatterns =
    {
        ".dll",
        ".exe",
        "appsettings",
        // Add more patterns as discovered
    };

    /// <summary>
    /// Checks if any file in the edit session would require restart.
    /// </summary>
    public static bool WouldRequireRestart(EditSession editSession)
    {
        return editSession.Files.Any(f =>
            SelfEditPatterns.Any(pattern => 
                f.RelativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets the appropriate phase after merge based on self-edit detection.
    /// </summary>
    public static WorkflowPhase GetPostMergePhase(EditSession editSession)
    {
        return WouldRequireRestart(editSession)
            ? WorkflowPhase.SelfEditWarning
            : WorkflowPhase.Rebooting;
    }
}
