using System.Text.Json;
using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class CodexConnectionService : IAsyncDisposable
{
    private const int MaxEvents = 500;

    private readonly object gate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly WorkspaceState workspaceState;
    private readonly SourceWorkspaceService sourceWorkspaceService;
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

    public CodexConnectionService(WorkspaceState workspaceState, SourceWorkspaceService sourceWorkspaceService)
    {
        this.workspaceState = workspaceState;
        this.sourceWorkspaceService = sourceWorkspaceService;
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

            client = new CodexAppServerClient();
            client.RawProtocol += line => AddRawLine(line);
            client.LogLine += line => AddEvent(statusEvents, "Log", null, "app-server", line);
            client.AssistantText += OnAssistantText;
            client.Telemetry += OnTelemetry;
            client.ToolActivity += OnToolActivity;
            client.Status += OnStatus;
            client.ServerRequest += OnServerRequest;
            client.Exited += code => AddEvent(statusEvents, "ServerExited", code == 0 ? "ok" : "error", "codex app-server", $"Exited with code {code}.");

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
                sendInitialContext: true,
                cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task SendTurnAsync(
        string repoRoot,
        string userPrompt,
        string model,
        string approvalPolicy,
        string sandbox,
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
                    sendInitialContext: true,
                    cancellationToken);
            }

            workspaceState.SetRepoRoot(repoRoot);
            string prompt = $$"""
        {{userPrompt}}

        Codex cwd:
        {{repoRoot}}

        Workspace boundary:
        - Treat the cwd above as the loaded workspace.
        - Do not use selected-file assumptions for this turn.
        - Use discovery, proposal, edit/diff, compile, and reindex order when work is requested.
        """;

            ClearTurnOutput();
            MarkTurnRunning();
            await activeClient.StartTurnAsync(
                prompt,
                repoRoot,
                model,
                NormalizeApprovalPolicy(approvalPolicy),
                NormalizeSandbox(sandbox),
                cancellationToken);
            AddEvent(statusEvents, "TurnPolicy", "ok", "codex", $"Turn policy set to approval={NormalizeApprovalPolicy(approvalPolicy)}, sandbox={NormalizeSandbox(sandbox)}, reviewer=user.");
        }
        finally
        {
            operationGate.Release();
        }
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
        AddCurrentTurnNotice(scope switch
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
        bool sendInitialContext,
        CancellationToken cancellationToken)
    {
        workspaceState.SetRepoRoot(repoRoot);
        await activeClient.StartThreadAsync(
            repoRoot,
            model,
            NormalizeApprovalPolicy(approvalPolicy),
            NormalizeSandbox(sandbox),
            cancellationToken);
        AddEvent(statusEvents, "ThreadPolicy", "ok", "codex", $"Thread policy set to approval={NormalizeApprovalPolicy(approvalPolicy)}, sandbox={NormalizeSandbox(sandbox)}.");

        if (!sendInitialContext)
        {
            return;
        }

        string? initialPrompt = TryBuildInitialWorkspacePrompt(repoRoot, out string contextStatus);
        AddEvent(statusEvents, "WorkspaceContext", initialPrompt is null ? "skipped" : "ok", "coding-services", contextStatus);
        if (initialPrompt is not null)
        {
            ClearTurnOutput();
            MarkTurnRunning();
            await activeClient.StartTurnAsync(
                initialPrompt,
                repoRoot,
                model,
                NormalizeApprovalPolicy(approvalPolicy),
                NormalizeSandbox(sandbox),
                cancellationToken);
            AddEvent(statusEvents, "TurnPolicy", "ok", "codex", $"Initial turn policy set to approval={NormalizeApprovalPolicy(approvalPolicy)}, sandbox={NormalizeSandbox(sandbox)}, reviewer=user.");
        }
    }

    private string? TryBuildInitialWorkspacePrompt(string repoRoot, out string status)
    {
        if (!Directory.Exists(repoRoot))
        {
            status = $"CWD does not exist: {repoRoot}";
            return null;
        }

        SourceWorkspaceStructureSnapshot snapshot;
        try
        {
            snapshot = sourceWorkspaceService.BuildProductStructureSnapshot(repoRoot, filter: null);
        }
        catch (Exception ex)
        {
            status = $"Could not build workspace context: {ex.Message}";
            return null;
        }

        if (!File.Exists(snapshot.WatchedSolutionPath))
        {
            status = $"No valid watched solution found for CWD: {repoRoot}";
            return null;
        }

        if (!File.Exists(snapshot.IndexDatabasePath) || snapshot.FileCount == 0 || snapshot.Tree.Count == 0)
        {
            status = string.IsNullOrWhiteSpace(snapshot.Message)
                ? "Watched solution index is missing, empty, or stale."
                : snapshot.Message;
            return null;
        }

        WorkspaceBootstrapContext context = new(
            Cwd: Path.GetFullPath(repoRoot),
            WatchedSolutionPath: snapshot.WatchedSolutionPath,
            IndexDatabasePath: snapshot.IndexDatabasePath,
            FileCount: snapshot.FileCount,
            ProjectCount: snapshot.Tree.Count,
            Projects: snapshot.Tree
                .Where(node => node.Kind.Equals("project", StringComparison.OrdinalIgnoreCase))
                .Select(ToBootstrapProject)
                .ToArray());

        string json = JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        status = $"Injected product workspace context for {context.ProjectCount} projects and {context.FileCount} indexed files.";
        return $$"""
        Initial product workspace context:
        ```json
        {{json}}
        ```

        Use this compiler/index-backed product project/file map as initial orientation for this thread.
        Test projects are intentionally not included in this initial context.
        The local MCP discovery surface advertises GetWorkspace, GetWatchedSolutionDigest, GetWatchedSolutionSummary, and GetTestProjectSummary.
        Call GetWatchedSolutionSummary when deeper full-solution type/member structure is needed.
        Call GetTestProjectSummary when test project structure is relevant.
        Treat the CWD as the loaded workspace.
        Do not edit files during this initialization turn.
        Reply with a concise workspace orientation: main projects, likely responsibility boundaries, and any context gaps.
        """;
    }

    private static BootstrapProject ToBootstrapProject(SourceTreeNode project)
    {
        List<BootstrapFile> files = [];
        CollectBootstrapFiles(project, files);
        return new BootstrapProject(
            Name: project.Name,
            Files: files
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FileCount: files.Count);
    }

    private static void CollectBootstrapFiles(SourceTreeNode node, List<BootstrapFile> files)
    {
        if (node.File is not null)
        {
            files.Add(new BootstrapFile(
                Path: node.File.RelativePath,
                Language: node.File.Language));
            return;
        }

        foreach (SourceTreeNode child in node.Children)
        {
            CollectBootstrapFiles(child, files);
        }
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
            || e.EventType.Equals("turn/failed", StringComparison.OrdinalIgnoreCase))
        {
            lock (gate)
            {
                isTurnRunning = false;
            }
        }

        AddEvent(statusEvents, e.EventType, null, "codex", e.Summary);
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

        if (!e.EventType.Equals("command completed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string status = IsFailureStatus(e.Status) ? "command failed" : "command completed";
        CodexPermissionRequest? request = permissionRequestService.ResolveLatestApproved(status);
        if (request is null)
        {
            return;
        }

        AddCurrentTurnNotice(IsFailureStatus(e.Status)
            ? $"Request #{request.RequestId} resumed after approval and the command failed."
            : $"Request #{request.RequestId} resumed after approval and the command completed.");
        AddEvent(
            statusEvents,
            "PermissionResult",
            status,
            "coding-services",
            $"Request #{request.RequestId} {status} after approval.");
    }

    private void OnTelemetry(TelemetryEvent e)
    {
        lock (gate)
        {
            telemetrySummary = telemetrySummary.Apply(e);
        }

        AddEvent(telemetryEvents, "Telemetry", null, "codex", e.Summary);
    }

    private void ClearTurnOutput()
    {
        lock (gate)
        {
            assistantText = string.Empty;
            currentTurnNoticeText = string.Empty;
            telemetryEvents.Clear();
            toolEvents.Clear();
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

public sealed record WorkspaceBootstrapContext(
    string Cwd,
    string WatchedSolutionPath,
    string IndexDatabasePath,
    int FileCount,
    int ProjectCount,
    IReadOnlyList<BootstrapProject> Projects);

public sealed record BootstrapProject(
    string Name,
    IReadOnlyList<BootstrapFile> Files,
    int FileCount);

public sealed record BootstrapFile(
    string Path,
    string Language);
