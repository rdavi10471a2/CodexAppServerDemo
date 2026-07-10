using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.ArchivedDiscussions;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Components.Pages.Home.Tasks;
using Markdig;
using Markdig.Extensions.MediaLinks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Radzen;
using System.Net;
using System.Text;

namespace CodexAppServerBlazor.Components.Pages.Home;

public partial class Home : IDisposable, IAsyncDisposable
{
    private const string CurrentUserId = "operator";
    private const string AssistantUserId = "codex";
    private const long MaxAttachmentBytes = 25L * 1024L * 1024L;

    private CodexConnectionSnapshot snapshot = new(
        false,
        null,
        false,
        string.Empty,
        string.Empty,
        CodexTelemetrySummary.Empty,
        [],
        [],
        [],
        [],
        []);

    private DirectoryBrowserSnapshot directorySnapshot = DirectoryBrowserSnapshot.Empty;
    private SourceWorkspaceSnapshot sourceSnapshot = SourceWorkspaceSnapshot.Empty("No workspace is selected.");
    private SourceWorkspaceSnapshot testSourceSnapshot = SourceWorkspaceSnapshot.Empty("No test project context is loaded.");
    private string codexExe = "codex";
    private string repoRoot = string.Empty;
    private string instanceLabel = string.Empty;
    private string sourceFilter = string.Empty;
    private string? selectedSourcePath;
    private int? selectedSourceLine;
    private string testSourceFilter = string.Empty;
    private string? selectedTestSourcePath;
    private int? selectedTestSourceLine;
    private string model = "gpt-5.4";
    private string approvalPolicy = "on-request";
    private string sandbox = "read-only";
    private WorkflowTurnMode turnMode = WorkflowTurnMode.Discuss;
    private string mcpUrl = McpHostFactory.DefaultLocalMcpUrl;
    private readonly List<TranscriptMessage> chatMessages = [];
    private readonly List<CodexTurnAttachment> turnAttachments = [];
    private string attachmentPickerKey = Guid.NewGuid().ToString("N");
    private string chatDraft = string.Empty;
    private string? activeAssistantMessageId;
    private string renderedAssistantText = string.Empty;
    private string? lastPermissionResultToastKey;
    private string? errorMessage;
    private bool showArchiveConversationPrompt;
    private ConversationContinuation pendingConversationContinuation;
    private string? pendingConversationTargetWorkspacePath;
    private string? pendingConversationCurrentWorkspacePath;
    private string archiveConversationMessage = string.Empty;
    private string archiveConversationTriggerLabel = string.Empty;
    private string archiveConversationContinueWithoutSavingText = "Continue";
    private string archiveConversationSaveButtonText = "Save";
    private string archiveConversationSuggestedName = string.Empty;
    private bool busy;
    private bool isRebuildingSourceIndex;
    private bool isConnectionPanelVisible = true;
    private bool isDirectoryBrowserVisible;
    private string? launchedReviewSessionId;
    private bool reviewDialogOpen;
    private bool reviewLaunchCheckInProgress;
    private bool reviewLaunchProcessing;
    private PendingReviewLaunchState? pendingReviewLaunch;
    private StagedReviewPageModel? validationGateModel;
    private int validationGatePendingCount;
    private TaskCompletionSource<bool>? validationGateCompletion;
    private string? reviewDialogSessionId;
    private TaskCompletionSource<bool>? reviewDialogCompletion;
    private int assistantViewVersion;
    private ElementReference controlGrid;
    private ElementReference connectionPane;
    private ElementReference workPanel;
    private ElementReference mainSplitter;
    private IJSObjectReference? mainResizeModule;
    private readonly List<CodexOutputEvent> debugEvents = [];
    private const int MaxDebugEvents = 300;
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private string TranscriptHtml => BuildTranscriptHtml();
    private string TranscriptBodyHtml => BuildTranscriptBodyHtml(includeMessageCopyButtons: true);
    private string TranscriptText => BuildTranscriptText();
    private string CurrentTurnHtml => RenderMarkdown(GetCurrentTurnText());
    private string assistantViewKey => $"{repoRoot}:{assistantViewVersion}";
    private IReadOnlyList<CodexOutputEvent> DebugEvents => debugEvents.ToArray();
    [Inject]
    public CodexConnectionService ConnectionService { get; set; } = default!;

    [Inject]
    public DirectoryBrowserService DirectoryBrowser { get; set; } = default!;

    [Inject]
    public SourceWorkspaceService SourceWorkspace { get; set; } = default!;

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    [Inject]
    public NotificationService NotificationService { get; set; } = default!;

    [Inject]
    public ITranscriptTaskPromotionService TranscriptTaskPromotionService { get; set; } = default!;

    [Inject]
    public IArchivedDiscussionService ArchivedDiscussionService { get; set; } = default!;

    [Inject]
    public DialogService DialogService { get; set; } = default!;

    [Inject]
    public WorkspaceSelectionService WorkspaceSelectionService { get; set; } = default!;

    [Inject]
    public WorkspaceState WorkspaceState { get; set; } = default!;

    [Inject]
    public IStagedReviewPageService StagedReviewPageService { get; set; } = default!;

    [Inject]
    public CodingServicesSettingsProvider SettingsProvider { get; set; } = default!;

    [Inject]
    public GovernedReviewCoordinatorService GovernedReviewCoordinator { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    protected override void OnInitialized()
    {
        ConnectionService.Changed += OnConnectionChanged;
        GovernedReviewCoordinator.Changed += OnGovernedReviewCoordinatorChanged;
        snapshot = ConnectionService.GetSnapshot();
        mcpUrl = Configuration["Mcp:Url"] ?? McpHostFactory.DefaultLocalMcpUrl;
        instanceLabel = (Configuration["AppInstance:Label"] ?? string.Empty).Trim();
        string configuredCwd = WorkspaceSelectionService.GetStartupWorkspace();
        SetWorkspace(configuredCwd);
        AddDebugEvent(
            "HomeInit",
            "ok",
            "coding-services",
            $"Initialized Home for workspace '{repoRoot}' with instance label '{instanceLabel}'.");
    }

    private async Task ToggleServer()
    {
        if (snapshot.IsServerStarted)
        {
            await RequestConversationDispositionAsync(ConversationContinuation.StopServer);
        }
        else
        {
            if (!ValidateWorkspaceForOperation("start Codex server"))
            {
                return;
            }

            await RunCommandAsync(() => ConnectionService.StartServerAsync(codexExe, CancellationToken.None));
        }
    }

    private async Task SendChatDraft()
    {
        if (busy || snapshot.IsTurnRunning)
        {
            errorMessage = "A Codex turn is already running. Wait for it to finish or resolve the pending agent action.";
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Warning,
                Summary = "Turn still running",
                Detail = errorMessage,
                Duration = 5000
            });
            return;
        }

        string content = chatDraft;
        if (string.IsNullOrWhiteSpace(content) && turnAttachments.Count == 0)
        {
            return;
        }

        if (!ValidateWorkspaceForOperation("send a turn"))
        {
            return;
        }

        CodexTurnAttachment[] attachments = turnAttachments.ToArray();
        if (!ValidateAttachmentsForSend(attachments))
        {
            return;
        }

        string effectiveSandbox = turnMode == WorkflowTurnMode.Work
            ? "read-only"
            : sandbox;
        if (!string.Equals(effectiveSandbox, sandbox, StringComparison.Ordinal))
        {
            sandbox = effectiveSandbox;
            AddDebugEvent(
                "TurnPolicyCoerced",
                "ok",
                "home",
                "Governed Work mode forced sandbox back to read-only before sending the turn.");
        }

        string transcriptContent = BuildUserTranscriptContent(content, attachments);
        ResetGovernedReviewStateForNewTurn(clearEditSessionId: turnMode == WorkflowTurnMode.Work);
        await RunCommandAsync(() => ConnectionService.SendTurnAsync(
            repoRoot,
            content,
            model,
            approvalPolicy,
            effectiveSandbox,
            turnMode,
            attachments,
            CancellationToken.None));
        if (errorMessage is not null)
        {
            return;
        }

        chatDraft = string.Empty;
        turnAttachments.Clear();
        attachmentPickerKey = Guid.NewGuid().ToString("N");
        AddUserMessage(transcriptContent);
        StartAssistantMessage("_Awaiting assistant response..._", snapshot.IsTurnRunning);
        RenderAssistantSnapshot(snapshot.IsTurnRunning);
    }

    private void ResetGovernedReviewStateForNewTurn(bool clearEditSessionId)
    {
        string? priorEditSessionId = WorkspaceState.CurrentEditSessionId;
        validationGateCompletion?.TrySetResult(false);
        validationGateCompletion = null;
        validationGateModel = null;
        validationGatePendingCount = 0;

        reviewDialogCompletion?.TrySetResult(true);
        reviewDialogCompletion = null;
        reviewDialogSessionId = null;

        pendingReviewLaunch = null;
        launchedReviewSessionId = null;
        reviewDialogOpen = false;
        reviewLaunchCheckInProgress = false;
        reviewLaunchProcessing = false;

        if (clearEditSessionId)
        {
            int retiredCount = RetirePendingArtifactsForSession(priorEditSessionId);
            WorkspaceState.SetCurrentEditSessionId(null);
            AddDebugEvent(
                "TurnPrep",
                "retire-session",
                "home",
                $"Retired {retiredCount} pending staged record(s) for previous edit session '{priorEditSessionId ?? "<none>"}' before starting a new Work turn.");
        }

        AddDebugEvent(
            "TurnPrep",
            "reset-governed-state",
            "home",
            $"Reset queued review, validation gate, and dialog state before starting a new {(turnMode == WorkflowTurnMode.Work ? "Work" : "Discuss")} turn. ClearedEditSessionId={clearEditSessionId}.");
    }

    private int RetirePendingArtifactsForSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
        {
            return 0;
        }

        try
        {
            WorkflowEditService workflowService = new(SettingsProvider.GetSettings(repoRoot));
            return workflowService.AbandonPendingSessionArtifacts(
                sessionId,
                "A new governed Work turn started before this pending edit session was resolved.");
        }
        catch (Exception ex)
        {
            AddDebugEvent(
                "TurnPrep",
                "retire-session-error",
                "home",
                $"Failed to retire pending edit-session artifacts for '{sessionId}': {ex.Message}");
            return 0;
        }
    }

    private async Task CreateTaskFromTranscript(string taskName)
    {
        if (!ValidateWorkspaceForOperation("create a task from chat"))
        {
            return;
        }

        if (turnMode != WorkflowTurnMode.Discuss)
        {
            errorMessage = "Create Task From Chat is only available in Discuss mode.";
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Warning,
                Summary = "Discuss mode required",
                Detail = errorMessage,
                Duration = 5000
            });
            return;
        }

        await RunCommandAsync(() =>
        {
            TranscriptTaskPromotionService.CreateTaskFromTranscript(
                repoRoot,
                taskName,
                TranscriptText);
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Task created",
                Detail = "Created a new task in New Task using the current chat transcript as user notes.",
                Duration = 5000
            });
            return Task.CompletedTask;
        });
    }

    private async Task AttachTurnFiles(InputFileChangeEventArgs args)
    {
        if (!ValidateWorkspaceForOperation("attach a file"))
        {
            return;
        }

        foreach (IBrowserFile file in args.GetMultipleFiles())
        {
            await SaveTurnAttachment(file);
        }

        attachmentPickerKey = Guid.NewGuid().ToString("N");
        await InvokeAsync(StateHasChanged);
    }

    private async Task SaveTurnAttachment(IBrowserFile file)
    {
        string safeName = GetSafeFileName(file.Name);
        string attachmentDirectory = Path.Combine(
            repoRoot,
            "runtime",
            "turn-attachments",
            DateTime.UtcNow.ToString("yyyyMMdd"),
            Guid.NewGuid().ToString("N"));
        string destinationPath = Path.Combine(attachmentDirectory, safeName);

        try
        {
            Directory.CreateDirectory(attachmentDirectory);

            Stream source = file.OpenReadStream(MaxAttachmentBytes);
            try
            {
                FileStream target = File.Create(destinationPath);
                try
                {
                    await source.CopyToAsync(target);
                }
                finally
                {
                    target.Dispose();
                }
            }
            finally
            {
                source.Dispose();
            }

            CodexTurnAttachmentKind kind = IsImageAttachment(safeName, file.ContentType)
                ? CodexTurnAttachmentKind.LocalImage
                : CodexTurnAttachmentKind.Text;
            turnAttachments.Add(new CodexTurnAttachment(
                safeName,
                destinationPath,
                kind,
                file.Size));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            string message = $"Could not attach {safeName}: {ex.Message}";
            errorMessage = message;
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "Attachment failed",
                Detail = message,
                Duration = 7000
            });
        }
    }

    private Task RemoveTurnAttachment(string path)
    {
        CodexTurnAttachment? attachment = turnAttachments.FirstOrDefault(candidate =>
            candidate.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (attachment is not null)
        {
            turnAttachments.Remove(attachment);
            TryDeleteRuntimeAttachment(attachment.Path);
        }

        attachmentPickerKey = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    private async Task DenyPermissionRequest(int requestId)
    {
        await RunCommandAsync(() => ConnectionService.DenyPermissionRequestAsync(
            requestId,
            cancelTurn: false,
            CancellationToken.None));
        if (errorMessage is null)
        {
            NotifyPermissionAction("Permission denied", "The agent can continue with a different approach.", NotificationSeverity.Warning);
        }
    }

    private async Task ApprovePermissionRequest(int requestId)
    {
        await RunCommandAsync(() => ConnectionService.ApprovePermissionRequestAsync(
            requestId,
            PermissionApprovalScope.Turn,
            CancellationToken.None));
        if (errorMessage is null)
        {
            NotifyPermissionAction("Permission granted", "The agent can continue this turn.", NotificationSeverity.Success);
        }
    }

    private async Task ApprovePermissionRequestForSession(int requestId)
    {
        await RunCommandAsync(() => ConnectionService.ApprovePermissionRequestAsync(
            requestId,
            PermissionApprovalScope.Session,
            CancellationToken.None));
        if (errorMessage is null)
        {
            NotifyPermissionAction("Permission granted for session", "Matching requests can continue for this session.", NotificationSeverity.Success);
        }
    }

    private async Task ApprovePermissionRequestAlways(int requestId)
    {
        await RunCommandAsync(() => ConnectionService.ApprovePermissionRequestAsync(
            requestId,
            PermissionApprovalScope.Persistent,
            CancellationToken.None));
        if (errorMessage is null)
        {
            NotifyPermissionAction("Permission granted always", "The proposed persistent rule was accepted.", NotificationSeverity.Success);
        }
    }

    private async Task CancelPermissionRequest(int requestId)
    {
        await RunCommandAsync(() => ConnectionService.DenyPermissionRequestAsync(
            requestId,
            cancelTurn: true,
            CancellationToken.None));
        if (errorMessage is null)
        {
            NotifyPermissionAction("Turn cancelled", "The permission request was denied and the turn was interrupted.", NotificationSeverity.Warning);
        }
    }

    private void BrowseForDirectory()
    {
        isDirectoryBrowserVisible = !isDirectoryBrowserVisible;
        if (isDirectoryBrowserVisible)
        {
            directorySnapshot = DirectoryBrowser.GetSnapshot(repoRoot);
        }
    }

    private void BrowseToDirectory(string path)
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(path);
    }

    private async Task UseBrowsedDirectory()
    {
        await ChangeWorkspaceAsync(directorySnapshot.CurrentPath);
        isDirectoryBrowserVisible = false;
    }

    private void CancelDirectoryBrowser()
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(repoRoot);
        isDirectoryBrowserVisible = false;
    }

    private bool ValidateWorkspaceForOperation(string operationName)
    {
        if (!string.IsNullOrWhiteSpace(repoRoot) && Directory.Exists(repoRoot))
        {
            return true;
        }

        string detail = string.IsNullOrWhiteSpace(repoRoot)
            ? "No CWD is selected."
            : $"CWD does not exist: {repoRoot}";
        string message = $"Cannot {operationName}. {detail}";
        errorMessage = message;
        NotificationService.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Error,
            Summary = "Invalid CWD",
            Detail = message,
            Duration = 7000
        });
        return false;
    }

    private async Task ChangeWorkspaceAsync(string? path)
    {
        string nextWorkspace = DirectoryBrowser.GetSnapshot(path).CurrentPath;
        string currentWorkspace = repoRoot;
        if (string.Equals(
                Path.GetFullPath(currentWorkspace),
                Path.GetFullPath(nextWorkspace),
                StringComparison.OrdinalIgnoreCase))
        {
            SetWorkspace(nextWorkspace);
            return;
        }

        if (snapshot.IsTurnRunning)
        {
            errorMessage = "Wait for the current turn to finish before changing the workspace.";
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Warning,
                Summary = "Turn still running",
                Detail = errorMessage,
                Duration = 5000
            });
            return;
        }

        await RequestConversationDispositionAsync(
            ConversationContinuation.ChangeWorkspace,
            nextWorkspace,
            currentWorkspace);
    }

    private void SetWorkspace(string? path)
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(path);
        repoRoot = directorySnapshot.CurrentPath;
        WorkspaceState.SetRepoRoot(repoRoot);
        WorkspaceState.SetCurrentEditSessionId(null);
        WorkspaceSelectionService.SaveWorkspace(repoRoot);
        launchedReviewSessionId = null;
        debugEvents.Clear();
        selectedSourcePath = null;
        selectedSourceLine = null;
        selectedTestSourcePath = null;
        selectedTestSourceLine = null;
        RefreshSourceSnapshot();
        RefreshTestSourceSnapshot();
        AddDebugEvent(
            "WorkspaceChanged",
            "ok",
            "coding-services",
            $"Workspace set to '{repoRoot}'. Cleared review session and local launch trace state.");
    }

    private void RefreshSourceSnapshot()
    {
        try
        {
            sourceSnapshot = SourceWorkspace.BuildSnapshot(repoRoot, selectedSourcePath, selectedSourceLine, sourceFilter);
            selectedSourcePath = sourceSnapshot.SelectedFile?.RelativePath;
            selectedSourceLine = sourceSnapshot.SelectedFile?.SelectedLine;
        }
        catch (Exception ex)
        {
            sourceSnapshot = SourceWorkspaceSnapshot.Empty(ex.Message);
            selectedSourcePath = null;
            selectedSourceLine = null;
        }
    }

    private async Task RebuildSourceIndex()
    {
        if (isRebuildingSourceIndex)
        {
            return;
        }

        try
        {
            isRebuildingSourceIndex = true;
            errorMessage = null;
            await SourceWorkspace.RebuildIndexAsync(repoRoot, CancellationToken.None);
            RefreshSourceSnapshot();
            RefreshTestSourceSnapshot();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isRebuildingSourceIndex = false;
        }
    }

    private void SelectSourceFile(SourceSelection selection)
    {
        selectedSourcePath = selection.RelativePath;
        selectedSourceLine = Math.Max(selection.Line, 1);
        RefreshSourceSnapshot();
    }

    private void ApplySourceFilter()
    {
        selectedSourcePath = null;
        selectedSourceLine = null;
        RefreshSourceSnapshot();
    }

    private void RefreshTestSourceSnapshot()
    {
        try
        {
            testSourceSnapshot = SourceWorkspace.BuildTestSnapshot(repoRoot, selectedTestSourcePath, selectedTestSourceLine, testSourceFilter);
            selectedTestSourcePath = testSourceSnapshot.SelectedFile?.RelativePath;
            selectedTestSourceLine = testSourceSnapshot.SelectedFile?.SelectedLine;
        }
        catch (Exception ex)
        {
            testSourceSnapshot = SourceWorkspaceSnapshot.Empty(ex.Message);
            selectedTestSourcePath = null;
            selectedTestSourceLine = null;
        }
    }

    private void SelectTestSourceFile(SourceSelection selection)
    {
        selectedTestSourcePath = selection.RelativePath;
        selectedTestSourceLine = Math.Max(selection.Line, 1);
        RefreshTestSourceSnapshot();
    }

    private void ApplyTestSourceFilter()
    {
        selectedTestSourcePath = null;
        selectedTestSourceLine = null;
        RefreshTestSourceSnapshot();
    }

    private void ToggleConnectionPanel()
    {
        isConnectionPanelVisible = !isConnectionPanelVisible;
    }

    private async Task ClearChatHistory()
    {
        await RequestConversationDispositionAsync(ConversationContinuation.StartNewConversation);
        await InvokeAsync(StateHasChanged);
    }

    private async Task RequestConversationDispositionAsync(
        ConversationContinuation continuation,
        string? targetWorkspacePath = null,
        string? currentWorkspacePath = null)
    {
        if (snapshot.IsTurnRunning)
        {
            string actionLabel = continuation switch
            {
                ConversationContinuation.StartNewConversation => "start a new conversation",
                ConversationContinuation.ChangeWorkspace => "change the workspace",
                ConversationContinuation.StopServer => "stop the server",
                _ => "continue"
            };
            errorMessage = $"Wait for the current turn to finish before you {actionLabel}.";
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Warning,
                Summary = "Turn still running",
                Detail = errorMessage,
                Duration = 5000
            });
            return;
        }

        if (!HasUnsavedConversationState())
        {
            await ContinueConversationActionAsync(continuation, targetWorkspacePath, currentWorkspacePath);
            return;
        }

        string targetLabel = continuation switch
        {
            ConversationContinuation.StartNewConversation => "start a new conversation",
            ConversationContinuation.ChangeWorkspace => "change the workspace",
            ConversationContinuation.StopServer => "stop the server",
            _ => "continue"
        };
        string continueWithoutSavingText = continuation switch
        {
            ConversationContinuation.StartNewConversation => "Restart Without Saving",
            ConversationContinuation.ChangeWorkspace => "Switch Workspace",
            ConversationContinuation.StopServer => "Stop Server",
            _ => "Continue"
        };
        string saveButtonText = continuation switch
        {
            ConversationContinuation.StartNewConversation => "Save",
            ConversationContinuation.ChangeWorkspace => "Save",
            ConversationContinuation.StopServer => "Save",
            _ => "Save"
        };

        PrepareArchiveConversationPrompt(
            continuation,
            targetWorkspacePath,
            currentWorkspacePath,
            BuildDispositionMessage(continuation, targetLabel),
            GetTriggerLabel(continuation, targetWorkspacePath),
            continueWithoutSavingText,
            saveButtonText,
            BuildSuggestedDiscussionName());
        await InvokeAsync(StateHasChanged);
    }

    private async Task ContinueConversationActionAsync(
        ConversationContinuation continuation,
        string? targetWorkspacePath = null,
        string? currentWorkspacePath = null)
    {
        switch (continuation)
        {
            case ConversationContinuation.StartNewConversation:
                if (snapshot.IsServerStarted && !string.IsNullOrWhiteSpace(snapshot.ThreadId))
                {
                    await RunCommandAsync(() => ConnectionService.ResetConversationAsync(
                        "Cleared the current chat history and reset the active Codex thread.",
                        CancellationToken.None));
                    if (errorMessage is not null)
                    {
                        return;
                    }
                }

                ResetLocalConversationState(clearDraft: true);
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Info,
                    Summary = "New conversation started",
                    Detail = "The visible chat was cleared and the next turn will start fresh.",
                    Duration = 4000
                });
                await InvokeAsync(StateHasChanged);
                break;

            case ConversationContinuation.ChangeWorkspace:
                bool shouldResetConversation = !string.IsNullOrWhiteSpace(snapshot.ThreadId)
                    || chatMessages.Count > 0
                    || turnAttachments.Count > 0
                    || !string.IsNullOrWhiteSpace(chatDraft);
                if (shouldResetConversation && snapshot.IsServerStarted)
                {
                    string currentWorkspace = currentWorkspacePath ?? repoRoot;
                    string nextWorkspace = targetWorkspacePath ?? repoRoot;
                    await RunCommandAsync(() => ConnectionService.ResetConversationAsync(
                        $"Reset the active Codex thread because the CWD changed from {currentWorkspace} to {nextWorkspace}.",
                        CancellationToken.None));
                    if (errorMessage is not null)
                    {
                        return;
                    }
                }

                await SourceWorkspace.EnsureWorkspaceArtifactsAsync(
                    targetWorkspacePath ?? repoRoot,
                    rebuildIndexIfMissing: true,
                    CancellationToken.None);
                ResetLocalConversationState(clearDraft: true);
                SetWorkspace(targetWorkspacePath);
                await InvokeAsync(StateHasChanged);
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Info,
                    Summary = "Workspace changed",
                    Detail = shouldResetConversation
                        ? "Changed the CWD and reset the current conversation so the next turn starts fresh."
                        : "Changed the CWD.",
                    Duration = 5000
                });
                break;

            case ConversationContinuation.StopServer:
                await RunCommandAsync(() => ConnectionService.StopServerAsync(CancellationToken.None));
                if (errorMessage is not null)
                {
                    return;
                }

                ResetLocalConversationState(clearDraft: true);
                await InvokeAsync(StateHasChanged);
                break;
        }
    }

    private void ResetLocalConversationState(bool clearDraft)
    {
        foreach (CodexTurnAttachment attachment in turnAttachments.ToArray())
        {
            TryDeleteRuntimeAttachment(attachment.Path);
        }

        chatMessages.Clear();
        turnAttachments.Clear();
        attachmentPickerKey = Guid.NewGuid().ToString("N");
        activeAssistantMessageId = null;
        renderedAssistantText = string.Empty;
        assistantViewVersion++;
        if (clearDraft)
        {
            chatDraft = string.Empty;
        }
    }

    private void PrepareArchiveConversationPrompt(
        ConversationContinuation continuation,
        string? targetWorkspacePath,
        string? currentWorkspacePath,
        string message,
        string triggerLabel,
        string continueWithoutSavingText,
        string saveButtonText,
        string suggestedName)
    {
        pendingConversationContinuation = continuation;
        pendingConversationTargetWorkspacePath = targetWorkspacePath;
        pendingConversationCurrentWorkspacePath = currentWorkspacePath;
        archiveConversationMessage = message;
        archiveConversationTriggerLabel = triggerLabel;
        archiveConversationContinueWithoutSavingText = continueWithoutSavingText;
        archiveConversationSaveButtonText = saveButtonText;
        archiveConversationSuggestedName = suggestedName;
        showArchiveConversationPrompt = true;
    }

    private void DismissArchiveConversationPrompt()
    {
        showArchiveConversationPrompt = false;
        pendingConversationTargetWorkspacePath = null;
        pendingConversationCurrentWorkspacePath = null;
        archiveConversationMessage = string.Empty;
        archiveConversationTriggerLabel = string.Empty;
        archiveConversationContinueWithoutSavingText = "Continue";
        archiveConversationSaveButtonText = "Save";
        archiveConversationSuggestedName = string.Empty;
    }

    private async Task CompleteArchiveConversationPrompt(ArchiveConversationDialogResult decision)
    {
        ConversationContinuation continuation = pendingConversationContinuation;
        string? targetWorkspacePath = pendingConversationTargetWorkspacePath;
        string? currentWorkspacePath = pendingConversationCurrentWorkspacePath;
        DismissArchiveConversationPrompt();

        if (decision.Decision == ArchiveConversationDecision.Cancel)
        {
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (decision.Decision == ArchiveConversationDecision.SaveAndContinue)
        {
            ArchivedDiscussionSaveResult saveResult;
            try
            {
                saveResult = ArchivedDiscussionService.SaveDiscussion(
                    new ArchivedDiscussionSaveRequest(
                        repoRoot,
                        decision.Name,
                        snapshot.ThreadId,
                        turnMode.ToString(),
                        GetTriggerLabel(continuation, targetWorkspacePath),
                        TranscriptText,
                        chatDraft,
                        turnAttachments.Select(attachment => attachment.Name).ToArray(),
                        DateTimeOffset.Now));
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Archive failed",
                    Detail = ex.Message,
                    Duration = 7000
                });
                await InvokeAsync(StateHasChanged);
                return;
            }

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Conversation archived",
                Detail = $"Saved to Archived Discussions as \"{saveResult.Name}\".",
                Duration = 5000
            });
        }

        await ContinueConversationActionAsync(continuation, targetWorkspacePath, currentWorkspacePath);
        await InvokeAsync(StateHasChanged);
    }

    private bool HasUnsavedConversationState()
    {
        return chatMessages.Count > 0
            || turnAttachments.Count > 0
            || !string.IsNullOrWhiteSpace(chatDraft);
    }

    private static string GetTriggerLabel(ConversationContinuation continuation, string? targetWorkspacePath)
    {
        return continuation switch
        {
            ConversationContinuation.StartNewConversation => "Start New Conversation",
            ConversationContinuation.ChangeWorkspace => string.IsNullOrWhiteSpace(targetWorkspacePath)
                ? "Change Workspace"
                : "Change Workspace to " + targetWorkspacePath,
            ConversationContinuation.StopServer => "Stop Server",
            _ => "Continue"
        };
    }

    private static string BuildDispositionMessage(ConversationContinuation continuation, string targetLabel)
    {
        return continuation switch
        {
            ConversationContinuation.StopServer =>
                "You have unsaved conversation state. Saving it now will make it easy to recover later as archived evidence before you stop the server.",
            _ =>
                $"You have unsaved conversation state that will be discarded if you {targetLabel}."
        };
    }

    private string BuildSuggestedDiscussionName()
    {
        foreach (TranscriptMessage message in chatMessages.Where(candidate => candidate.IsUser))
        {
            string firstLine = message.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                return firstLine.Length <= 72
                    ? firstLine
                    : firstLine[..72].TrimEnd();
            }
        }

        if (!string.IsNullOrWhiteSpace(chatDraft))
        {
            string draft = chatDraft.Trim();
            return draft.Length <= 72
                ? draft
                : draft[..72].TrimEnd();
        }

        return "Saved discussion " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        mainResizeModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/sourceResize.js");
        await mainResizeModule.InvokeVoidAsync(
            "setBeforeUnloadGuard",
            ShouldWarnBeforeUnload(),
            "Refreshing or closing this page will reset the current Coding Services session.");

        if (isConnectionPanelVisible)
        {
            await mainResizeModule.InvokeVoidAsync(
                "attachMainSplitter",
                controlGrid,
                connectionPane,
                workPanel,
                mainSplitter);
        }

    }

    private bool ShouldWarnBeforeUnload()
    {
        return snapshot.IsServerStarted
            || snapshot.IsTurnRunning
            || !string.IsNullOrWhiteSpace(snapshot.ThreadId);
    }

    private async Task RunCommandAsync(Func<Task> command)
    {
        try
        {
            busy = true;
            errorMessage = null;
            await command();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            busy = false;
            snapshot = ConnectionService.GetSnapshot();
        }
    }

    private void OnConnectionChanged()
    {
        snapshot = ConnectionService.GetSnapshot();
        RenderAssistantSnapshot(isStreaming: snapshot.IsTurnRunning);
        _ = InvokeAsync(async () =>
        {
            CodexOutputEvent? latestStatus = snapshot.StatusEvents.LastOrDefault();
            AddDebugEvent(
                "ConnectionChanged",
                snapshot.IsTurnRunning ? "running" : "idle",
                "home",
                $"turnRunning={snapshot.IsTurnRunning}; threadId={snapshot.ThreadId ?? "<none>"}; permissions={snapshot.PermissionRequests.Count}; currentEditSessionId={WorkspaceState.CurrentEditSessionId ?? "<none>"}; launchedReviewSessionId={launchedReviewSessionId ?? "<none>"}; queuedReviewSessionId={pendingReviewLaunch?.SessionId ?? "<none>"}; latestStatus={(latestStatus is null ? "<none>" : latestStatus.Type + "|" + (latestStatus.Status ?? "-") + "|" + latestStatus.Detail)}");
            NotifyLatestPermissionResult();
            StateHasChanged();
        });
    }

    private void OnGovernedReviewCoordinatorChanged()
    {
        _ = InvokeAsync(async () =>
        {
            GovernedReviewPendingRequest? pendingRequest = GovernedReviewCoordinator.GetPendingRequest();
            if (pendingRequest is null)
            {
                pendingReviewLaunch = null;
                StateHasChanged();
                return;
            }

            pendingReviewLaunch = new PendingReviewLaunchState(
                pendingRequest.Request.SessionId,
                pendingRequest.Request.PendingCount);
            AddDebugEvent(
                "ReviewCoordinator",
                "queued",
                "home",
                $"Coordinator queued governed review for edit session '{pendingRequest.Request.SessionId}' with {pendingRequest.Request.PendingCount} pending item(s).");
            await ProcessQueuedReviewLaunchAsync();
            StateHasChanged();
        });
    }

    private async Task TryLaunchPendingReviewAsync(bool isTurnRunning, CodexOutputEvent? latestStatus)
    {
        if (pendingReviewLaunch is not null && !reviewDialogOpen && !reviewLaunchProcessing)
        {
            AddDebugEvent(
                "ReviewLaunchCheck",
                "queued",
                "home",
                $"Launch already queued for edit session '{pendingReviewLaunch.SessionId}'. Waiting for the render cycle to drain it.");
            return;
        }

        if (reviewLaunchCheckInProgress)
        {
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                "Launch suppressed because another review launch check is already in progress.");
            return;
        }

        if (reviewDialogOpen)
        {
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                $"Launch suppressed because the review dialog is already open for session '{launchedReviewSessionId ?? "<none>"}'.");
            return;
        }

        reviewLaunchCheckInProgress = true;

        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
        {
            pendingReviewLaunch = null;
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                $"Launch suppressed because workspace root is unavailable: '{repoRoot}'.");
            reviewLaunchCheckInProgress = false;
            return;
        }

        string? currentEditSessionId = WorkspaceState.CurrentEditSessionId;
        if (string.IsNullOrWhiteSpace(currentEditSessionId))
        {
            pendingReviewLaunch = null;
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                "Launch suppressed because there is no current edit session id.");
            reviewLaunchCheckInProgress = false;
            return;
        }

        IReadOnlyList<StagedReviewQueueItem> pendingReviews;
        try
        {
            pendingReviews = StagedReviewPageService.ListPending(repoRoot);
        }
        catch (Exception ex)
        {
            AddDebugEvent(
                "ReviewLaunchCheck",
                "error",
                "home",
                $"ListPending failed for workspace '{repoRoot}': {ex.Message}");
            reviewLaunchCheckInProgress = false;
            return;
        }

        pendingReviews = pendingReviews
            .Where(review => review.SessionId.Equals(currentEditSessionId, StringComparison.Ordinal))
            .ToArray();

        AddDebugEvent(
            "ReviewLaunchCheck",
            "inspect",
            "home",
            $"currentEditSessionId={currentEditSessionId}; launchedReviewSessionId={launchedReviewSessionId ?? "<none>"}; pendingCount={pendingReviews.Count}; stagedIds={(pendingReviews.Count == 0 ? "<none>" : string.Join(", ", pendingReviews.Select(review => review.StagedRecordId)))}");

        if (pendingReviews.Count == 0)
        {
            pendingReviewLaunch = null;
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                $"Launch suppressed because no pending reviews exist for edit session '{currentEditSessionId}'.");
            reviewLaunchCheckInProgress = false;
            return;
        }

        StagedReviewPageModel nextModel;
        try
        {
            nextModel = StagedReviewPageService.LoadNextForSession(repoRoot, currentEditSessionId);
        }
        catch (Exception ex)
        {
            AddDebugEvent(
                "ReviewLaunchCheck",
                "error",
                "home",
                $"LoadNextForSession failed for edit session '{currentEditSessionId}': {ex.Message}");
            reviewLaunchCheckInProgress = false;
            return;
        }

        if (nextModel.IsSessionComplete)
        {
            pendingReviewLaunch = null;
            AddDebugEvent(
                "ReviewLaunchCheck",
                "skipped",
                "home",
                $"Launch suppressed because edit session '{currentEditSessionId}' is already complete.");
            reviewLaunchCheckInProgress = false;
            return;
        }

        pendingReviewLaunch = new PendingReviewLaunchState(currentEditSessionId, pendingReviews.Count);
        AddDebugEvent(
            "ReviewLaunch",
            "queued",
            "home",
            $"Queued governed review for edit session '{currentEditSessionId}' with {pendingReviews.Count} pending item(s). ValidationError={nextModel.PreMergeValidationIsError}; ForceApproved={nextModel.PreMergeValidationForceApproved}; RelativePath='{nextModel.RelativePath}'.");
        reviewLaunchCheckInProgress = false;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();

        if (pendingReviewLaunch is not null && !reviewDialogOpen && !reviewLaunchProcessing)
        {
            AddDebugEvent(
                "ReviewLaunch",
                "fallback-drain",
                "home",
                $"Render-cycle drain did not fire for edit session '{currentEditSessionId}'. Invoking queued review launch directly.");
            await InvokeAsync(ProcessQueuedReviewLaunchAsync);
        }
    }

    private bool ShouldSuppressReviewLaunchWhileTurnRunning(CodexOutputEvent? latestStatus)
    {
        if (!snapshot.IsTurnRunning)
        {
            return false;
        }

        if (snapshot.PermissionRequests.Count > 0)
        {
            return true;
        }

        return !HasAssistantCompletionSignal(latestStatus);
    }

    private bool HasAssistantCompletionSignal(CodexOutputEvent? latestStatus)
    {
        if (latestStatus is not null
            && latestStatus.Type.Equals("assistant", StringComparison.OrdinalIgnoreCase)
            && latestStatus.Detail.StartsWith("Assistant response completed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        for (int index = snapshot.StatusEvents.Count - 1; index >= 0; index--)
        {
            CodexOutputEvent statusEvent = snapshot.StatusEvents[index];
            if (statusEvent.Type.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                return statusEvent.Detail.StartsWith("Assistant response completed", StringComparison.OrdinalIgnoreCase);
            }

            if (statusEvent.Type.Equals("turn/completed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (statusEvent.Type.Equals("thread/status/changed", StringComparison.OrdinalIgnoreCase)
                && statusEvent.Detail.Contains("\"type\":\"idle\"", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> OpenValidationGateAsync(StagedReviewPageModel model, int pendingCount)
    {
        NotificationService.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Warning,
            Summary = "Pre-merge validation failed",
            Detail = $"Review is required for {model.RelativePath} before governed merge can continue.",
            Duration = 7000
        });

        validationGateModel = model;
        validationGatePendingCount = pendingCount;
        validationGateCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await InvokeAsync(StateHasChanged);

        AddDebugEvent(
            "ValidationGate",
            "open-dialog",
            "home",
            $"Dispatching blocking validation dialog for edit session '{model.SessionId}' on '{model.RelativePath}'. PendingCount={pendingCount}.");

        _ = InvokeAsync(async () =>
        {
            try
            {
                await DialogService.OpenAsync<PreMergeValidationDialog>(
                    "Pre-merge validation gate",
                    new Dictionary<string, object?>
                    {
                        [nameof(PreMergeValidationDialog.RelativePath)] = model.RelativePath,
                        [nameof(PreMergeValidationDialog.ValidationStatus)] = model.PreMergeValidationStatus,
                        [nameof(PreMergeValidationDialog.PendingCount)] = pendingCount,
                        [nameof(PreMergeValidationDialog.DecisionMade)] = EventCallback.Factory.Create<bool>(this, HandleValidationGateDecisionAsync)
                    },
                    new DialogOptions
                    {
                        Width = "88vw",
                        Height = "88vh",
                        CloseDialogOnEsc = false,
                        CloseDialogOnOverlayClick = false,
                        Resizable = true,
                        Draggable = true,
                        ShowClose = false
                    });
            }
            catch (Exception ex)
            {
                AddDebugEvent(
                    "ValidationGate",
                    "open-error",
                    "home",
                    $"Validation dialog open failed for edit session '{model.SessionId}': {ex.Message}");
                validationGateCompletion?.TrySetResult(false);
            }
        });

        bool continueResult = await validationGateCompletion.Task;
        validationGateModel = null;
        validationGatePendingCount = 0;
        validationGateCompletion = null;
        await InvokeAsync(StateHasChanged);
        return continueResult;
    }

    private async Task ProcessQueuedReviewLaunchAsync()
    {
        PendingReviewLaunchState? launch = pendingReviewLaunch;
        if (launch is null || reviewDialogOpen || reviewLaunchProcessing)
        {
            return;
        }

        AddDebugEvent(
            "ReviewLaunch",
            "draining-queue",
            "home",
            $"Draining queued governed review for edit session '{launch.SessionId}'.");

        pendingReviewLaunch = null;
        reviewLaunchProcessing = true;

        try
        {
            StagedReviewPageModel model = StagedReviewPageService.LoadNextForSession(repoRoot, launch.SessionId);
            if (model.IsSessionComplete)
            {
                AddDebugEvent(
                    "ReviewLaunch",
                    "skipped",
                    "home",
                    $"Queued launch for edit session '{launch.SessionId}' was skipped because the session is already complete.");
                return;
            }

            if (model.PreMergeValidationIsError && !model.PreMergeValidationForceApproved)
            {
                AddDebugEvent(
                    "ValidationGate",
                    "opening",
                    "home",
                    $"Opening pre-merge validation gate for edit session '{launch.SessionId}' on '{model.RelativePath}'.");

                bool continueToReview = await OpenValidationGateAsync(model, launch.PendingCount);
                AddDebugEvent(
                    "ValidationGate",
                    continueToReview ? "continue" : "dismissed",
                    "home",
                    $"Validation gate result for edit session '{launch.SessionId}': continue={continueToReview}.");

                if (!continueToReview)
                {
                    GovernedReviewCoordinator.Complete(
                        launch.SessionId,
                        new GovernedReviewResolution(
                            launch.SessionId,
                            Completed: false,
                            AcceptedWithOverride: false,
                            RemainingPendingCount: launch.PendingCount,
                            Message: $"Governed review for edit session '{launch.SessionId}' was deferred at the pre-merge validation gate. The staged candidate remains pending."));
                    return;
                }
            }

            launchedReviewSessionId = launch.SessionId;
            reviewDialogOpen = true;

            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Governed review ready",
                Detail = $"Opening in-app review for {model.RelativePath}.",
                Duration = 4000
            });

            AddDebugEvent(
                "ReviewLaunch",
                "opening-modal",
                "home",
                $"Opening staged review dialog for edit session '{launch.SessionId}' with {launch.PendingCount} pending item(s).");
            await OpenStagedReviewDialogAsync(launch.SessionId);

            IReadOnlyList<StagedReviewQueueItem> remainingReviews = StagedReviewPageService.ListPending(repoRoot)
                .Where(review => review.SessionId.Equals(launch.SessionId, StringComparison.Ordinal))
                .ToArray();

            AddDebugEvent(
                "ReviewLaunch",
                remainingReviews.Count == 0 ? "completed" : "closed",
                "home",
                $"Review dialog closed for edit session '{launch.SessionId}'. Remaining pending item(s): {remainingReviews.Count}.");
            GovernedReviewCoordinator.Complete(
                launch.SessionId,
                new GovernedReviewResolution(
                    launch.SessionId,
                    Completed: remainingReviews.Count == 0,
                    AcceptedWithOverride: model.PreMergeValidationIsError,
                    RemainingPendingCount: remainingReviews.Count,
                    Message: remainingReviews.Count == 0
                        ? $"Governed review completed for edit session '{launch.SessionId}'."
                        : $"Governed review dialog closed before edit session '{launch.SessionId}' was fully resolved. {remainingReviews.Count} staged item(s) remain pending."));
        }
        catch (Exception ex)
        {
            AddDebugEvent(
                "ReviewLaunch",
                "error",
                "home",
                $"Review dialog failed for edit session '{launch.SessionId}': {ex.Message}");
            GovernedReviewCoordinator.Complete(
                launch.SessionId,
                new GovernedReviewResolution(
                    launch.SessionId,
                    Completed: false,
                    AcceptedWithOverride: false,
                    RemainingPendingCount: launch.PendingCount,
                    Message: $"Governed review failed to open for edit session '{launch.SessionId}': {ex.Message}"));
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Error,
                Summary = "Review launch failed",
                Detail = ex.Message,
                Duration = 7000
            });
        }
        finally
        {
            launchedReviewSessionId = null;
            reviewDialogOpen = false;
            reviewLaunchProcessing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task HandleValidationGateDecisionAsync(bool allowReview)
    {
        TaskCompletionSource<bool>? completion = validationGateCompletion;
        validationGateModel = null;
        validationGatePendingCount = 0;
        validationGateCompletion = null;
        completion?.TrySetResult(allowReview);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OpenStagedReviewDialogAsync(string sessionId)
    {
        AddDebugEvent(
            "ReviewDialog",
            "open-requested",
            "home",
            $"Opening staged review dialog for session '{sessionId}'.");
        reviewDialogSessionId = sessionId;
        reviewDialogCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await InvokeAsync(StateHasChanged);

        AddDebugEvent(
            "ReviewDialog",
            "open-dialog",
            "home",
            $"Dispatching blocking staged review dialog for session '{sessionId}'.");

        _ = InvokeAsync(async () =>
        {
            try
            {
                await DialogService.OpenAsync<StagedReviewDialog>(
                    "Merge Review",
                    new Dictionary<string, object?>
                    {
                        [nameof(StagedReviewDialog.WorkspaceRoot)] = repoRoot,
                        [nameof(StagedReviewDialog.SessionId)] = sessionId,
                        [nameof(StagedReviewDialog.AutoCloseWhenComplete)] = true,
                        [nameof(StagedReviewDialog.TraceEventRaised)] = EventCallback.Factory.Create<string>(this, HandleStagedReviewTraceAsync),
                        [nameof(StagedReviewDialog.CloseRequested)] = EventCallback.Factory.Create(this, HandleStagedReviewDialogClosedAsync)
                    },
                    new DialogOptions
                    {
                        Width = "88vw",
                        Height = "88vh",
                        CloseDialogOnEsc = false,
                        CloseDialogOnOverlayClick = false,
                        Resizable = true,
                        Draggable = true,
                        ShowClose = false
                    });
            }
            catch (Exception ex)
            {
                AddDebugEvent(
                    "ReviewDialog",
                    "open-error",
                    "home",
                    $"Staged review dialog open failed for session '{sessionId}': {ex.Message}");
                reviewDialogCompletion?.TrySetResult(true);
            }
        });

        AddDebugEvent(
            "ReviewDialog",
            "awaiting-close",
            "home",
            $"Awaiting staged review dialog completion for session '{sessionId}'.");
        if (reviewDialogCompletion is not null)
        {
            await reviewDialogCompletion.Task;
        }

        AddDebugEvent(
            "ReviewDialog",
            "await-complete",
            "home",
            $"Staged review dialog completion returned for session '{sessionId}'.");
    }

    private async Task HandleStagedReviewDialogClosedAsync()
    {
        AddDebugEvent(
            "ReviewDialog",
            "close-callback",
            "home",
            $"Staged review dialog requested close for session '{reviewDialogSessionId ?? "<none>"}'.");
        TaskCompletionSource<bool>? completion = reviewDialogCompletion;
        reviewDialogSessionId = null;
        reviewDialogCompletion = null;
        completion?.TrySetResult(true);
        DialogService.Close();
        await InvokeAsync(StateHasChanged);
    }

    private Task HandleStagedReviewTraceAsync(string traceMessage)
    {
        string[] parts = traceMessage.Split('|', 2, StringSplitOptions.None);
        string action = parts.Length > 0 ? parts[0] : "trace";
        string detail = parts.Length > 1 ? parts[1] : traceMessage;
        AddDebugEvent(
            "ReviewDialog",
            action,
            "dialog",
            detail);
        return Task.CompletedTask;
    }

    private void AddDebugEvent(string type, string? status, string source, string detail)
    {
        debugEvents.Add(new CodexOutputEvent(
            DateTimeOffset.Now,
            type,
            status,
            source,
            detail,
            status?.Equals("error", StringComparison.OrdinalIgnoreCase) == true ? "error" : "info"));
        if (debugEvents.Count > MaxDebugEvents)
        {
            debugEvents.RemoveRange(0, debugEvents.Count - MaxDebugEvents);
        }
    }

    private void AddUserMessage(string content)
    {
        chatMessages.Add(new TranscriptMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = CurrentUserId,
            Role = "user",
            IsUser = true,
            Content = content,
            Timestamp = DateTime.Now
        });
    }

    private void StartAssistantMessage(string initialContent, bool isStreaming)
    {
        activeAssistantMessageId = Guid.NewGuid().ToString("N");
        renderedAssistantText = initialContent;
        chatMessages.Add(new TranscriptMessage
        {
            Id = activeAssistantMessageId,
            UserId = AssistantUserId,
            Role = "assistant",
            IsUser = false,
            IsStreaming = isStreaming,
            Content = initialContent,
            Timestamp = DateTime.Now
        });
    }

    private void RenderAssistantSnapshot(bool isStreaming)
    {
        if (string.IsNullOrEmpty(activeAssistantMessageId))
        {
            return;
        }

        TranscriptMessage? message = chatMessages.FirstOrDefault(candidate => candidate.Id == activeAssistantMessageId);
        if (message is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.AssistantText))
        {
            renderedAssistantText = snapshot.AssistantText;
            message.Content = renderedAssistantText;
        }

        message.IsStreaming = isStreaming;
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildTranscriptHtml()
    {
        var html = new StringBuilder();
        html.Append(GetTranscriptDocumentStart());
        html.Append(BuildTranscriptBodyHtml(includeMessageCopyButtons: false));
        html.Append("</body></html>");
        return html.ToString();
    }

    private string BuildTranscriptBodyHtml(bool includeMessageCopyButtons)
    {
        var html = new StringBuilder();

        if (chatMessages.Count == 0)
        {
            html.Append("""
            <div class="empty">
                <div class="empty-icon">□</div>
                <div>Start a workspace conversation.</div>
            </div>
            """);
        }
        else
        {
            foreach (TranscriptMessage message in chatMessages)
            {
                string role = message.IsUser ? "user" : "assistant";
                string label = message.IsUser ? "You" : "Codex";
                string timestamp = WebUtility.HtmlEncode(message.Timestamp.ToString("HH:mm:ss"));
                string messageText = WebUtility.HtmlEncode(message.Content);
                html.Append("<article class=\"message ");
                html.Append(role);
                html.Append("\"><header class=\"message-header\"><span>");
                html.Append(label);
                html.Append("</span><div class=\"message-meta\"><span>");
                html.Append(timestamp);
                html.Append("</span>");
                if (includeMessageCopyButtons)
                {
                    html.Append("<button type=\"button\" class=\"message-copy\" data-copy-text=\"");
                    html.Append(messageText);
                    html.Append("\" title=\"Copy this message\">Copy</button>");
                }

                html.Append("</div></header><div class=\"message-body\">");
                html.Append(RenderMarkdown(message.Content));
                html.Append("</div></article>");
            }
        }

        return html.ToString();
    }

    private static string GetTranscriptDocumentStart()
    {
        return """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
        :root {
            color-scheme: light;
            font-family: "Segoe UI", Arial, sans-serif;
            color: #16202c;
            background: #ffffff;
        }
        * {
            box-sizing: border-box;
        }
        body {
            margin: 0;
            padding: 14px;
            font-size: 14px;
            line-height: 1.45;
        }
        .empty {
            min-height: calc(100vh - 28px);
        }
        .transcript-body {
            padding: 14px;
        }
        .empty {
            display: grid;
            place-content: center;
            gap: 8px;
            color: #6c7d8f;
            text-align: center;
        }
        .empty-icon {
            font-size: 34px;
        }
        .message {
            max-width: 1180px;
            margin: 0 0 12px;
            border: 1px solid #d7e1ea;
            border-radius: 7px;
            background: #ffffff;
            overflow: hidden;
        }
        .message.user {
            background: #f7fafc;
        }
        .message.assistant {
            background: #ffffff;
        }
        .message-header {
            display: flex;
            justify-content: space-between;
            gap: 10px;
            border-bottom: 1px solid #e4ebf1;
            padding: 7px 10px;
            color: #526a82;
            font-size: 12px;
            font-weight: 800;
            text-transform: uppercase;
        }
        .message-body {
            padding: 10px 12px;
            overflow-wrap: anywhere;
        }
        .message-body p:first-child {
            margin-top: 0;
        }
        .message-body p:last-child {
            margin-bottom: 0;
        }
        .message-body ul,
        .message-body ol {
            padding-left: 22px;
        }
        .message-body a {
            color: #4169e1;
            text-decoration: none;
        }
        .message-body a:hover {
            text-decoration: underline;
        }
        code, pre {
            font-family: Consolas, "Courier New", monospace;
        }
        code {
            border-radius: 4px;
            padding: 1px 4px;
            background: #eef3f7;
        }
        pre {
            overflow: auto;
            border-radius: 6px;
            padding: 10px;
            background: #0f1722;
            color: #d9e6f2;
            white-space: pre;
        }
        pre code {
            padding: 0;
            background: transparent;
            color: inherit;
        }
        </style>
        </head>
        <body>
        """;
    }

    private string BuildTranscriptText()
    {
        var text = new StringBuilder();
        foreach (TranscriptMessage message in chatMessages)
        {
            string label = message.IsUser ? "You" : "Codex";
            if (text.Length > 0)
            {
                text.AppendLine();
                text.AppendLine();
            }

            text.Append(label);
            text.Append(" ");
            text.AppendLine(message.Timestamp.ToString("HH:mm:ss"));
            text.AppendLine(message.Content);
        }

        return text.ToString();
    }

    private string GetCurrentTurnText()
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(snapshot.CurrentTurnNoticeText))
        {
            text.AppendLine(snapshot.CurrentTurnNoticeText);
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(activeAssistantMessageId))
        {
            TranscriptMessage? message = chatMessages.FirstOrDefault(candidate => candidate.Id == activeAssistantMessageId);
            if (message?.IsStreaming == true)
            {
                text.Append(message.Content);
            }
        }

        return text.ToString();
    }

    private bool ValidateAttachmentsForSend(IReadOnlyList<CodexTurnAttachment> attachments)
    {
        foreach (CodexTurnAttachment attachment in attachments)
        {
            if (!File.Exists(attachment.Path))
            {
                string message = $"Attachment is missing from runtime storage: {attachment.Name}";
                errorMessage = message;
                NotificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Attachment missing",
                    Detail = message,
                    Duration = 7000
                });
                return false;
            }
        }

        return true;
    }

    private static string BuildUserTranscriptContent(string content, IReadOnlyList<CodexTurnAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return content;
        }

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(content))
        {
            text.AppendLine(content.TrimEnd());
            text.AppendLine();
        }

        text.AppendLine("Attachments:");
        foreach (CodexTurnAttachment attachment in attachments)
        {
            string kind = attachment.Kind == CodexTurnAttachmentKind.LocalImage ? "image" : "file";
            text.Append("- ");
            text.Append(attachment.Name);
            text.Append(" (");
            text.Append(kind);
            text.Append(", ");
            text.Append(FormatAttachmentSize(attachment.SizeBytes));
            text.AppendLine(")");
        }

        return text.ToString();
    }

    private static string FormatAttachmentSize(long sizeBytes)
    {
        if (sizeBytes >= 1024L * 1024L)
        {
            return $"{sizeBytes / 1024d / 1024d:0.##} MB";
        }

        if (sizeBytes >= 1024L)
        {
            return $"{sizeBytes / 1024d:0.##} KB";
        }

        return $"{sizeBytes} bytes";
    }

    private static bool IsImageAttachment(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string extension = Path.GetExtension(fileName);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "attachment";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private static void TryDeleteRuntimeAttachment(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void NotifyPermissionAction(string summary, string detail, NotificationSeverity severity)
    {
        NotificationService.Notify(new NotificationMessage
        {
            Severity = severity,
            Summary = summary,
            Detail = detail,
            Duration = 3500
        });
    }

    private void NotifyLatestPermissionResult()
    {
        CodexPermissionRequest? request = snapshot.PermissionRequests
            .Where(candidate => candidate.ResolvedAt is not null)
            .OrderBy(candidate => candidate.ResolvedAt)
            .LastOrDefault();
        if (request is null)
        {
            return;
        }

        bool completed = request.Status.Contains("completed", StringComparison.OrdinalIgnoreCase);
        bool failed = request.Status.Contains("failed", StringComparison.OrdinalIgnoreCase);
        if (!completed && !failed)
        {
            return;
        }

        string key = $"{request.RequestId}:{request.Status}:{request.ResolvedAt:O}";
        if (string.Equals(lastPermissionResultToastKey, key, StringComparison.Ordinal))
        {
            return;
        }

        bool changeResult = request.Status.Contains("change", StringComparison.OrdinalIgnoreCase);
        lastPermissionResultToastKey = key;
        NotifyPermissionAction(
            completed
                ? (changeResult ? "Approved change completed" : "Approved command completed")
                : (changeResult ? "Approved change failed" : "Approved command failed"),
            $"Request #{request.RequestId} resumed after approval.",
            completed ? NotificationSeverity.Success : NotificationSeverity.Error);
    }

    private static string RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, MarkdownPipeline);
    }

    private sealed record PendingReviewLaunchState(string SessionId, int PendingCount);

    public void Dispose()
    {
        ConnectionService.Changed -= OnConnectionChanged;
        GovernedReviewCoordinator.Changed -= OnGovernedReviewCoordinatorChanged;
    }

    public async ValueTask DisposeAsync()
    {
        if (mainResizeModule is not null)
        {
            try
            {
                await mainResizeModule.InvokeVoidAsync("setBeforeUnloadGuard", false, string.Empty);
                await mainResizeModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}

public sealed class TranscriptMessage
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public bool IsStreaming { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public enum ConversationContinuation
{
    StartNewConversation,
    ChangeWorkspace,
    StopServer
}
