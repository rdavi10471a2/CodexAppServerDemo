using CodexAppServerWinForms;
using CodexAppServerWinForms.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class CodexConnectionService : IAsyncDisposable
{
    private const int MaxEvents = 500;
    private const string SupportedApprovalPolicy = "never";
    private const string SupportedSandbox = "danger-full-access";

    private readonly object gate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly WorkspaceState workspaceState;
    private CodexAppServerClient? client;
    private string assistantText = string.Empty;
    private CodexTelemetrySummary telemetrySummary = CodexTelemetrySummary.Empty;
    private readonly List<CodexOutputEvent> statusEvents = [];
    private readonly List<CodexOutputEvent> telemetryEvents = [];
    private readonly List<CodexOutputEvent> toolEvents = [];
    private readonly List<string> rawLines = [];

    public CodexConnectionService(WorkspaceState workspaceState)
    {
        this.workspaceState = workspaceState;
    }

    public event Action? Changed;

    public CodexConnectionSnapshot GetSnapshot()
    {
        lock (gate)
        {
            return new CodexConnectionSnapshot(
                IsServerStarted: client?.IsStarted == true,
                ThreadId: client?.ThreadId,
                AssistantText: assistantText,
                TelemetrySummary: telemetrySummary,
                StatusEvents: statusEvents.ToArray(),
                TelemetryEvents: telemetryEvents.ToArray(),
                ToolEvents: toolEvents.ToArray(),
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
        client.ToolActivity += e => AddEvent(toolEvents, e.EventType, e.Status, e.Name, e.Detail ?? string.Empty);
        client.Status += e => AddEvent(statusEvents, e.EventType, null, "codex", e.Summary);
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
                    telemetryEvents.Clear();
                    toolEvents.Clear();
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
        workspaceState.SetRepoRoot(repoRoot);
        await activeClient.StartThreadAsync(
            repoRoot,
            model,
            SupportedApprovalPolicy,
            SupportedSandbox,
            cancellationToken);
        AddEvent(statusEvents, "ThreadPolicy", "ok", "codex", $"Thread policy forced to approval={SupportedApprovalPolicy}, sandbox={SupportedSandbox}.");
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
            workspaceState.SetRepoRoot(repoRoot);
            await activeClient.StartThreadAsync(
                repoRoot,
                model,
                SupportedApprovalPolicy,
                SupportedSandbox,
                cancellationToken);
            AddEvent(statusEvents, "ThreadPolicy", "ok", "codex", $"Thread policy forced to approval={SupportedApprovalPolicy}, sandbox={SupportedSandbox}.");
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
        await activeClient.StartTurnAsync(prompt, cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private CodexAppServerClient GetStartedClient()
    {
        if (client is null || !client.IsStarted)
        {
            throw new InvalidOperationException("Start codex app-server first.");
        }

        return client;
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
            telemetryEvents.Clear();
            toolEvents.Clear();
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
    string AssistantText,
    CodexTelemetrySummary TelemetrySummary,
    IReadOnlyList<CodexOutputEvent> StatusEvents,
    IReadOnlyList<CodexOutputEvent> TelemetryEvents,
    IReadOnlyList<CodexOutputEvent> ToolEvents,
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
