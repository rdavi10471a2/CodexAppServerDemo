// =============================================================================
// CodexConnectionService.cs - MODIFICATIONS
// Part of: CodeHands_Implementation_Proposal.md
// Location: docs/proposed/
// Target: CodexAppServerBlazor/Services/CodexConnectionService.cs
// Description: Integrate WorkflowStateManager and EditSessionBridge
// References: 03_WorkflowStateManager.cs, 05_EditSessionBridge.cs
// =============================================================================

// CHANGES:
// 1. Add new dependencies to constructor
// 2. Add new methods for edit session management
// 3. Integrate state tracking into existing methods

// ADD to constructor parameters:
/*
public CodexConnectionService(
    // ... existing parameters ...
    IWorkflowStateManager workflowStateManager,        // NEW
    FileCapabilitiesAnalyzer fileCapabilitiesAnalyzer, // NEW
    // ... existing parameters ...
)
{
    // ... existing assignments ...
    _workflowStateManager = workflowStateManager;
    _editSessionBridge = new EditSessionBridge(editService, workflowStateManager);
    _fileCapabilitiesAnalyzer = fileCapabilitiesAnalyzer;
}
*/

// ADD new fields:
/*
private readonly IWorkflowStateManager _workflowStateManager;
private readonly EditSessionBridge _editSessionBridge;
private readonly FileCapabilitiesAnalyzer _fileCapabilitiesAnalyzer;
*/

// ADD new methods:

/// <summary>
/// Starts an edit session with the given files.
/// </summary>
public void StartEditSession(IReadOnlyList<string> filePaths)
{
    if (workflowSessionState == null)
    {
        throw new InvalidOperationException("Start a thread first.");
    }
    
    _workflowStateManager.StartEditSession(workflowSessionState, filePaths, _fileCapabilitiesAnalyzer);
}

/// <summary>
/// Signals that editing is complete and starts overlay build.
/// </summary>
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

/// <summary>
/// Runs the overlay build against the working copies.
/// </summary>
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

/// <summary>
/// Runs dotnet build against the working copies.
/// </summary>
private async Task<(int ExitCode, string Output)> RunDotNetBuildAsync(WorkflowSessionState session)
{
    // TODO: Integration with MSBuild/DotNetBuildRunner from AICodingServices
    throw new NotImplementedException("Integration with MSBuild/DotNetBuildRunner required");
}

/// <summary>
/// Marks a file as merged.
/// </summary>
public void MarkFileMerged(string filePath)
{
    if (workflowSessionState == null) return;
    _editSessionBridge.OnFileMerged(workflowSessionState, filePath);
}

/// <summary>
/// Rejects a file with a reason.
/// </summary>
public void RejectFile(string filePath, string reason)
{
    if (workflowSessionState == null) return;
    _editSessionBridge.OnFileRejected(workflowSessionState, filePath, reason);
}

/// <summary>
/// Gets the current edit session summary.
/// </summary>
public EditSessionSummary GetEditSessionSummary()
{
    if (workflowSessionState == null)
    {
        return new EditSessionSummary(null, 0, 0, 0, 0, [], WorkflowPhase.Idle);
    }
    
    return _workflowStateManager.GetEditSessionSummary(workflowSessionState);
}

// UPDATE SendTurnAsync to track turn count:
/*
public async Task SendTurnAsync(...)
{
    // ... existing code ...
    
    // Track turn count and timestamp
    if (workflowSessionState != null)
    {
        workflowSessionState.TurnCount++;
        workflowSessionState.LastTurnAt = DateTimeOffset.UtcNow;
    }
    
    // ... rest of existing code ...
}
*/

// UPDATE StartThreadAsync to initialize extended session state:
/*
public async Task StartThreadAsync(...)
{
    // ... existing code ...
    
    if (workflowSessionState == null)
    {
        workflowSessionState = new WorkflowSessionState(repoRoot, mode);
        // Initialize extended properties if needed
        workflowSessionState.CurrentPhase = WorkflowPhase.Idle;
    }
    
    // ... rest of existing code ...
}
*/
