using CodexAppServerWinForms;
using CodexAppServerWinForms.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class CodexConnectionService : IAsyncDisposable
{
    private const int MaxLines = 300;
    private const string SupportedApprovalPolicy = "never";
    private const string SupportedSandbox = "danger-full-access";

    private readonly object gate = new();
    private readonly SelectedFileState selectedFileState;
    private CodexAppServerClient? client;
    private string assistantText = string.Empty;
    private readonly List<string> statusLines = [];
    private readonly List<string> telemetryLines = [];
    private readonly List<string> toolLines = [];
    private readonly List<string> rawLines = [];

    public CodexConnectionService(SelectedFileState selectedFileState)
    {
        this.selectedFileState = selectedFileState;
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
                StatusLines: statusLines.ToArray(),
                TelemetryLines: telemetryLines.ToArray(),
                ToolLines: toolLines.ToArray(),
                RawLines: rawLines.ToArray());
        }
    }

    public async Task StartServerAsync(string codexExe, CancellationToken cancellationToken)
    {
        if (client?.IsStarted == true)
        {
            return;
        }

        client = new CodexAppServerClient();
        client.RawProtocol += line => AddLine(rawLines, line);
        client.LogLine += line => AddLine(statusLines, line);
        client.AssistantText += OnAssistantText;
        client.Telemetry += e => AddLine(telemetryLines, e.Summary);
        client.ToolActivity += e => AddLine(toolLines, $"{e.EventType}: {e.Name} [{e.Status}] {e.Detail}");
        client.Status += e => AddLine(statusLines, $"{e.EventType}: {e.Summary}");
        client.Exited += code => AddLine(statusLines, $"server: codex app-server exited with code {code}.");

        await client.StartAsync(codexExe, cancellationToken);
        AddLine(statusLines, "Blazor connection initialized codex app-server.");
    }

    public async Task StartThreadAsync(
        string repoRoot,
        string model,
        string approvalPolicy,
        string sandbox,
        CancellationToken cancellationToken)
    {
        CodexAppServerClient activeClient = GetStartedClient();
        selectedFileState.SetRepoRoot(repoRoot);
        await activeClient.StartThreadAsync(
            repoRoot,
            model,
            SupportedApprovalPolicy,
            SupportedSandbox,
            cancellationToken);
        AddLine(statusLines, $"Thread policy forced to approval={SupportedApprovalPolicy}, sandbox={SupportedSandbox}.");
    }

    public async Task SendTurnAsync(
        string repoRoot,
        string userPrompt,
        string model,
        string approvalPolicy,
        string sandbox,
        CancellationToken cancellationToken)
    {
        CodexAppServerClient activeClient = GetStartedClient();
        if (string.IsNullOrWhiteSpace(activeClient.ThreadId))
        {
            await StartThreadAsync(repoRoot, model, approvalPolicy, sandbox, cancellationToken);
        }

        selectedFileState.SetRepoRoot(repoRoot);
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

    private void ClearTurnOutput()
    {
        lock (gate)
        {
            assistantText = string.Empty;
            telemetryLines.Clear();
            toolLines.Clear();
        }

        Changed?.Invoke();
    }

    private void AddLine(List<string> target, string line)
    {
        lock (gate)
        {
            target.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (target.Count > MaxLines)
            {
                target.RemoveRange(0, target.Count - MaxLines);
            }
        }

        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }
}

public sealed record CodexConnectionSnapshot(
    bool IsServerStarted,
    string? ThreadId,
    string AssistantText,
    IReadOnlyList<string> StatusLines,
    IReadOnlyList<string> TelemetryLines,
    IReadOnlyList<string> ToolLines,
    IReadOnlyList<string> RawLines);