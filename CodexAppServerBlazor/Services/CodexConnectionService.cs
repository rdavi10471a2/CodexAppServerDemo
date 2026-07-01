using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Services;

public sealed class CodexConnectionService : IAsyncDisposable
{
    private const int MaxEvents = 500;

    private readonly object gate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Func<CodexAppServerClient> clientFactory;
    private readonly WorkspaceState workspaceState;
    private readonly IWorkspaceWorkflowContextService workspaceWorkflowContextService;
    private readonly ITaskWorkflowContextService taskWorkflowContextService;
    private readonly IWorkflowTurnContextComposer workflowTurnContextComposer;
    private readonly PermissionRequestService permissionRequestService = new();
    private CodexAppServerClient? client;
    private string assistantText = string.Empty;
    private string currentTurnNoticeText = string.Empty;
    private bool isTurnRunning;
    private CodexTelemetrySummary telemetrySummary = CodexTelemetrySummary.Empty;
    private readonly List<CodexOutputEvent> statusEvents = [];
    private readonly List<CodexOutputEvent> telemetryEvents = [];
    private readonly List<CodexOutputEvent> toolEvents = [];
    private readonly List<string> rawLines = [];
    private WorkflowSessionState? workflowSessionState;

    public CodexConnectionService(
        WorkspaceState workspaceState,
        IWorkspaceWorkflowContextService workspaceWorkflowContextService,
        ITaskWorkflowContextService taskWorkflowContextService,
        IWorkflowTurnContextComposer workflowTurnContextComposer,
        Func<CodexAppServerClient>? clientFactory = null)
    {
        this.clientFactory = clientFactory ?? (() => new CodexAppServerClient());
        this.workspaceState = workspaceState;
        this.workspaceWorkflowContextService = workspaceWorkflowContextService;
        this.taskWorkflowContextService = taskWorkflowContextService;
        this.workflowTurnContextComposer = workflowTurnContextComposer;
    }

    public event Action? Changed;

    public CodexConnectionSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new CodexConnectionSnapshot(
                IsServerStarted: client?.IsStarted == true,
                ThreadId: client?.ThreadId,
                IsTurnRunning: isTurnRunning,
                AssistantText: assistantText,
                CurrentTurnNoticeText: currentTurnNoticeText,
                TelemetrySummary: telemetrySummary,
                StatusEvents: statusEvents.ToArray(),
                TelemetryEvents: telemetryEvents.ToArray(),
                ToolEvents: toolEvents.ToArray(),
                PermissionRequests: permissionRequestService.GetSnapshot(),
                RawLines: rawLines.ToArray());
        }
    }

    public async Task StartServerAsync(string codexExe, CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A Codex operation is already running.");
        }

        try
        {
            if (client?.IsStarted == true)
            {
                return;
            }

            client = clientFactory();
            client.RawProtocol += line => AddRawLine(line);
            client.LogLine += line => AddEvent(statusEvents, "Log", null, "app-server", line);
            client.AssistantText += OnAssistantText;
            client.Telemetry += OnTelemetry;
            client.ToolActivity += OnToolActivity;
            client.Status += OnStatus;
            client.ServerRequest += OnServerRequest;
            client.Exited += OnClientExited;

            await client.StartAsync(codexExe, cancellationToken);
            AddEvent(statusEvents, "ServerStarted", "ok", "codex app-server", "Blazor connection initialized codex app-server.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task StopServerAsync(CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A Codex operation is already running.");
        }

        try
        {
            if (client is null)
            {
                return;
            }

            await client.DisposeAsync();
            client = null;
            lock (gate)
            {
                assistantText = string.Empty;
                currentTurnNoticeText = string.Empty;
                telemetryEvents.Clear();
                toolEvents.Clear();
                permissionRequestService.Clear();
                isTurnRunning = false;
                workflowSessionState = null;
            }

            AddEvent(statusEvents, "ServerStopped", "ok", "codex app-server", "Stopped codex app-server.");
        }
        finally
        {
            operationGate.Release();
            Changed?.Invoke();
        }
    }

    public async Task StartThreadAsync(
        string repoRoot,
        string model,
        string approvalPolicy,
        string sandbox,
        WorkflowTurnMode mode,
        CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A Codex operation is already running.");
        }

        try
        {
            CodexAppServerClient activeClient = GetStartedClient();
            await StartThreadCoreAsync(
                activeClient,
                repoRoot,
                model,
                approvalPolicy,
                sandbox,
                mode,
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task ResetConversationAsync(string reason, CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A Codex operation is already running.");
        }

        try
        {
            CodexAppServerClient activeClient = GetStartedClient();
            if (isTurnRunning)
            {
                throw new InvalidOperationException("Wait for the current turn to finish before resetting the conversation.");
            }

            lock (gate)
            {
                assistantText = string.Empty;
                currentTurnNoticeText = string.Empty;
                permissionRequestService.Clear();
                workflowSessionState = null;
            }

            activeClient.ResetThreadState();
            AddEvent(
                statusEvents,
                "ThreadReset",
                "ok",
                "coding-services",
                string.IsNullOrWhiteSpace(reason)
                    ? "Cleared the active Codex thread and workflow session."
                    : reason);
        }
        finally
        {
            operationGate.Release();
            Changed?.Invoke();
        }
    }

    public async Task SendTurnAsync(
        string repoRoot,
        string userPrompt,
        string model,
        string approvalPolicy,
        string sandbox,
        WorkflowTurnMode mode,
        IReadOnlyList<CodexTurnAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        if (!await operationGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A Codex operation is already running.");
        }

        try
        {
            CodexAppServerClient activeClient = GetStartedClient();
            if (string.IsNullOrWhiteSpace(activeClient.ThreadId))
            {
                await StartThreadCoreAsync(
                    activeClient,
                    repoRoot,
                    model,
                    approvalPolicy,
                    sandbox,
                    mode,
                    cancellationToken);
            }

            workspaceState.SetRepoRoot(repoRoot);
            workflowSessionState ??= new WorkflowSessionState(repoRoot, mode);
            WorkflowPromptSection workspaceContext = workspaceWorkflowContextService.BuildTurnContext(repoRoot);
            WorkflowTurnTaskContext taskContext = mode == WorkflowTurnMode.Work
                ? taskWorkflowContextService.BuildTurnContext(repoRoot)
                : new WorkflowTurnTaskContext(null, "Task context skipped because mode is Discuss.", null);
            WorkflowTurnEnvelope envelope = workflowTurnContextComposer.Compose(
                userPrompt,
                repoRoot,
                mode,
                workflowSessionState,
                workspaceContext,
                taskContext);
            if (!workflowSessionState.HasAttachedWorkspaceContext)
            {
                AddEvent(
                    statusEvents,
                    "WorkspaceContext",
                    workspaceContext.HasPrompt ? "ok" : "skipped",
                    "coding-services",
                    workspaceContext.Status);
            }
            AddEvent(
                statusEvents,
                "TaskContext",
                taskContext.HasPrompt ? "ok" : "skipped",
                "coding-services",
                taskContext.Status);

            ClearTurnOutput();
            MarkTurnRunning();
            if (attachments is { Count: > 0 })
            {
                AddEvent(statusEvents, "TurnAttachments", "ok", "coding-services", BuildAttachmentStatus(attachments));
            }

            try
            {
                await activeClient.StartTurnAsync(
                    envelope.Prompt,
                    repoRoot,
                    model,
                    NormalizeApprovalPolicy(approvalPolicy),
                    NormalizeSandbox(sandbox),
                    attachments,
                    cancellationToken);
            }
            catch
            {
                lock (gate)
                {
                    isTurnRunning = false;
                }

                Changed?.Invoke();
                throw;
            }
            if (envelope.IncludedWorkspaceContext)
            {
                workflowSessionState.HasAttachedWorkspaceContext = true;
            }

            AddEvent(statusEvents, "TurnMode", "ok", "coding-services", $"Turn mode set to {mode}.");
            AddEvent(statusEvents, "TurnPolicy", "ok", "codex", $"Turn policy set to approval={NormalizeApprovalPolicy(approvalPolicy)}, sandbox={NormalizeSandbox(sandbox)}, reviewer=user.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    private static string BuildAttachmentStatus(IReadOnlyList<CodexTurnAttachment> attachments)
    {
        return string.Join(
            "; ",
            attachments.Select(attachment =>
                $"{(attachment.Kind == CodexTurnAttachmentKind.LocalImage ? "localImage" : "text")}: {attachment.Name} ({attachment.SizeBytes} bytes) -> {attachment.Path}"));
    }

    public void ReportStatus(string type, string? status, string source, string detail)
    {
        AddEvent(statusEvents, type, status, source, detail);
    }

    public async Task DenyPermissionRequestAsync(
        int requestId,
        bool cancelTurn,
        CancellationToken cancellationToken)
    {
        CodexAppServerClient activeClient = GetStartedClient();
        CodexPermissionRequest? request = permissionRequestService.Resolve(
            requestId,
            cancelTurn ? "cancelled" : "denied");
        if (request is null)
        {
            throw new InvalidOperationException($"No pending permission request found for id {requestId}.");
        }

        object response = PermissionRequestService.CreateDenyResponse(request.Method, cancelTurn);
        await activeClient.RespondToServerRequestAsync(request.RequestId, response, cancellationToken);
        AddCurrentTurnNotice(cancelTurn
            ? $"Cancelled request #{request.RequestId}; waiting for the turn to stop."
            : $"Denied request #{request.RequestId}; waiting for the agent to continue.");
        AddEvent(
            statusEvents,
            "PermissionResponse",
            request.Status,
            "coding-services",
            $"Responded to {request.Method} with {PermissionRequestService.DescribeResponse(response)}");
    }

    public async Task ApprovePermissionRequestAsync(
        int requestId,
        PermissionApprovalScope scope,
        CancellationToken cancellationToken)
    {
        CodexAppServerClient activeClient = GetStartedClient();
        CodexPermissionRequest? request = permissionRequestService.Resolve(requestId, scope switch
        {
            PermissionApprovalScope.Session => "approved for session",
            PermissionApprovalScope.Persistent => "approved always",
            _ => "approved"
        });
        if (request is null)
        {
            throw new InvalidOperationException($"No pending permission request found for id {requestId}.");
        }

        object response = PermissionRequestService.CreateApproveResponse(request.Method, request.RawJson, scope);
        await activeClient.RespondToServerRequestAsync(request.RequestId, response, cancellationToken);
        AddCurrentTurnNotice(PermissionRequestService.IsElicitationRequest(request.Method)
            ? $"Answered elicitation request #{request.RequestId}; waiting for the agent to continue."
            : scope switch
        {
            PermissionApprovalScope.Session => $"Approved request #{request.RequestId} for this session; waiting for the command result.",
            PermissionApprovalScope.Persistent => $"Approved request #{request.RequestId} always; waiting for the command result.",
            _ => $"Approved request #{request.RequestId}; waiting for the command result."
        });
        AddEvent(
            statusEvents,
            "PermissionResponse",
            request.Status,
            "coding-services",
            $"Responded to {request.Method} with {PermissionRequestService.DescribeResponse(response)}");
    }

    private CodexAppServerClient GetStartedClient()
    {
        if (client is null || !client.IsStarted)
        {
            throw new InvalidOperationException("Start codex app-server first.");
        }

        return client;
    }

    private async Task StartThreadCoreAsync(
        CodexAppServerClient activeClient,
        string repoRoot,
        string model,
        string approvalPolicy,
        string sandbox,
        WorkflowTurnMode mode,
        CancellationToken cancellationToken)
    {
        workspaceState.SetRepoRoot(repoRoot);
        await activeClient.StartThreadAsync(
            repoRoot,
            model,
            NormalizeApprovalPolicy(approvalPolicy),
            NormalizeSandbox(sandbox),
            cancellationToken);
        workflowSessionState = new WorkflowSessionState(repoRoot, mode);
        AddEvent(statusEvents, "ThreadPolicy", "ok", "codex", $"Thread policy set to approval={NormalizeApprovalPolicy(approvalPolicy)}, sandbox={NormalizeSandbox(sandbox)}.");
        AddEvent(statusEvents, "ThreadMode", "ok", "coding-services", $"Thread initialized in {mode} mode.");
        AddEvent(statusEvents, "WorkspaceContext", "ready", "coding-services", "Brief indexed workspace context will be attached to the next eligible user turn.");
    }

    private void OnAssistantText(AssistantTextEvent e)
    {
        lock (gate)
        {
            if (e.IsFinal)
            {
                assistantText = e.Text;
            }
            else
            {
                assistantText += e.Text;
            }
        }

        Changed?.Invoke();
    }

    private void OnStatus(StatusEvent e)
    {
        if (e.EventType.Equals("turn/completed", StringComparison.OrdinalIgnoreCase)
            || e.EventType.Equals("turn/failed", StringComparison.OrdinalIgnoreCase)
            || IsThreadIdleStatus(e))
        {
            lock (gate)
            {
                isTurnRunning = false;
            }
        }

        AddEvent(statusEvents, e.EventType, null, "codex", e.Summary);
    }

    private static bool IsThreadIdleStatus(StatusEvent e)
    {
        return e.EventType.Equals("thread/status/changed", StringComparison.OrdinalIgnoreCase)
            && e.Summary.Contains("\"type\":\"idle\"", StringComparison.OrdinalIgnoreCase);
    }

    private void OnServerRequest(CodexServerRequestEvent e)
    {
        CodexPermissionRequest request = permissionRequestService.Add(e);
        AddEvent(
            statusEvents,
            "PermissionRequest",
            "pending",
            "codex app-server",
            $"{request.Method} #{request.RequestId}: {request.Summary}");
    }

    private void OnToolActivity(ToolEvent e)
    {
        AddEvent(toolEvents, e.EventType, e.Status, e.Name, e.Detail ?? string.Empty);

        bool commandCompleted = e.EventType.Equals("command completed", StringComparison.OrdinalIgnoreCase);
        bool fileChangeCompleted = e.EventType.Equals("file change completed", StringComparison.OrdinalIgnoreCase);
        if (!commandCompleted && !fileChangeCompleted)
        {
            return;
        }

        bool failed = IsFailureStatus(e.Status);
        string noun = fileChangeCompleted ? "change" : "command";
        string status = failed ? $"{noun} failed" : $"{noun} completed";
        CodexPermissionRequest? request = permissionRequestService.ResolveCommandResult(e.CorrelationId, e.ApprovalId, status);
        if (request is null)
        {
            return;
        }

        AddCurrentTurnNotice(failed
            ? $"Request #{request.RequestId} resumed after approval and the {noun} failed."
            : $"Request #{request.RequestId} resumed after approval and the {noun} completed.");
        if (failed && !string.IsNullOrWhiteSpace(e.Detail))
        {
            AddCurrentTurnNotice(BuildFailureDiagnosticNotice(e.Detail));
        }
        AddEvent(
            statusEvents,
            "PermissionResult",
            status,
            "coding-services",
            BuildPermissionResultDetail(request.RequestId, status, e.Detail));
    }

    private void OnTelemetry(TelemetryEvent e)
    {
        lock (gate)
        {
            telemetrySummary = telemetrySummary.Apply(e);
        }

        AddEvent(telemetryEvents, "Telemetry", null, "codex", e.Summary);
    }

    private void OnClientExited(int code)
    {
        lock (gate)
        {
            isTurnRunning = false;
        }

        AddEvent(statusEvents, "ServerExited", code == 0 ? "ok" : "error", "codex app-server", $"Exited with code {code}.");
    }

    private void ClearTurnOutput()
    {
        lock (gate)
        {
            assistantText = string.Empty;
            currentTurnNoticeText = string.Empty;
        }

        Changed?.Invoke();
    }

    private void AddCurrentTurnNotice(string detail)
    {
        lock (gate)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {detail}";
            currentTurnNoticeText = string.IsNullOrWhiteSpace(currentTurnNoticeText)
                ? line
                : $"{currentTurnNoticeText}{Environment.NewLine}{line}";
        }

        Changed?.Invoke();
    }

    private void MarkTurnRunning()
    {
        lock (gate)
        {
            isTurnRunning = true;
        }

        Changed?.Invoke();
    }

    private void AddEvent(List<CodexOutputEvent> target, string type, string? status, string source, string detail)
    {
        CodexOutputEvent outputEvent = new(
            Timestamp: DateTimeOffset.Now,
            Type: string.IsNullOrWhiteSpace(type) ? "Event" : type,
            Status: status,
            Source: string.IsNullOrWhiteSpace(source) ? "codex" : source,
            Detail: detail,
            Severity: ClassifySeverity(type, status, detail));

        lock (gate)
        {
            target.Add(outputEvent);
            if (target.Count > MaxEvents)
            {
                target.RemoveRange(0, target.Count - MaxEvents);
            }
        }

        Changed?.Invoke();
    }

    private void AddRawLine(string line)
    {
        lock (gate)
        {
            rawLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (rawLines.Count > MaxEvents)
            {
                rawLines.RemoveRange(0, rawLines.Count - MaxEvents);
            }
        }

        Changed?.Invoke();
    }

    private static string ClassifySeverity(string? type, string? status, string? detail)
    {
        string text = string.Join(" ", type, status, detail);
        if (ContainsAny(text, "error", "failed", "exception", "unauthorized", "token"))
        {
            return "error";
        }

        if (ContainsAny(text, "warning", "warn", "rate", "retry"))
        {
            return "warning";
        }

        if (ContainsAny(text, "ok", "succeeded", "success", "completed"))
        {
            return "success";
        }

        return "info";
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFailureStatus(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
            ContainsAny(status, "error", "fail", "failed", "cancel", "denied");
    }

    private static string BuildPermissionResultDetail(int requestId, string status, string? detail)
    {
        string prefix = $"Request #{requestId} {status} after approval.";
        if (string.IsNullOrWhiteSpace(detail))
        {
            return prefix;
        }

        return prefix + Environment.NewLine + Environment.NewLine + detail.Trim();
    }

    private static string BuildFailureDiagnosticNotice(string detail)
    {
        string firstLine = detail
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?? "Command failed after approval.";
        return "Diagnostic: " + firstLine;
    }

    private static string NormalizeApprovalPolicy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "untrusted" => "untrusted",
            "on-failure" => "on-failure",
            "on-request" => "on-request",
            "never" => "never",
            _ => "on-request"
        };
    }

    private static string NormalizeSandbox(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "read-only" => "read-only",
            "workspace-write" => "workspace-write",
            "danger-full-access" => "danger-full-access",
            _ => "read-only"
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        operationGate.Dispose();
    }
}

public sealed record CodexConnectionSnapshot(
    bool IsServerStarted,
    string? ThreadId,
    bool IsTurnRunning,
    string AssistantText,
    string CurrentTurnNoticeText,
    CodexTelemetrySummary TelemetrySummary,
    IReadOnlyList<CodexOutputEvent> StatusEvents,
    IReadOnlyList<CodexOutputEvent> TelemetryEvents,
    IReadOnlyList<CodexOutputEvent> ToolEvents,
    IReadOnlyList<CodexPermissionRequest> PermissionRequests,
    IReadOnlyList<string> RawLines);

public sealed record CodexOutputEvent(
    DateTimeOffset Timestamp,
    string Type,
    string? Status,
    string Source,
    string Detail,
    string Severity);

public sealed record CodexTelemetrySummary(
    int? InputTokens,
    int? CachedInputTokens,
    int? OutputTokens,
    int? ReasoningOutputTokens,
    int? ModelContextWindow,
    int? PrimaryUsedPercent,
    int? SecondaryUsedPercent,
    string? PlanType)
{
    public static CodexTelemetrySummary Empty { get; } = new(null, null, null, null, null, null, null, null);

    public double? ContextUsedPercent =>
        InputTokens.HasValue && ModelContextWindow.HasValue && ModelContextWindow.Value > 0
            ? InputTokens.Value * 100.0 / ModelContextWindow.Value
            : null;

    public CodexTelemetrySummary Apply(TelemetryEvent e)
    {
        return this with
        {
            InputTokens = e.InputTokens ?? InputTokens,
            CachedInputTokens = e.CachedInputTokens ?? CachedInputTokens,
            OutputTokens = e.OutputTokens ?? OutputTokens,
            ReasoningOutputTokens = e.ReasoningOutputTokens ?? ReasoningOutputTokens,
            ModelContextWindow = e.ModelContextWindow ?? ModelContextWindow,
            PrimaryUsedPercent = e.PrimaryUsedPercent ?? PrimaryUsedPercent,
            SecondaryUsedPercent = e.SecondaryUsedPercent ?? SecondaryUsedPercent,
            PlanType = e.PlanType ?? PlanType
        };
    }
}
