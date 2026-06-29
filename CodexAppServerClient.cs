using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexAppServerBlazor;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonRpcResponse>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string, CancellationToken, Task>? rawSender;
    private Process? _process;
    private int _nextId;

    public CodexAppServerClient(Func<string, CancellationToken, Task>? rawSender = null)
    {
        this.rawSender = rawSender;
    }

    public event Action<string>? RawProtocol;
    public event Action<string>? LogLine;
    public event Action<AssistantTextEvent>? AssistantText;
    public event Action<TelemetryEvent>? Telemetry;
    public event Action<ToolEvent>? ToolActivity;
    public event Action<StatusEvent>? Status;
    public event Action<CodexServerRequestEvent>? ServerRequest;
    public event Action<int>? Exited;

    public bool IsStarted => _process is { HasExited: false };
    public string? ThreadId { get; private set; }

    public async Task StartAsync(string codexExe = "codex", CancellationToken cancellationToken = default)
    {
        if (IsStarted)
            return;

        var psi = new ProcessStartInfo
        {
            FileName = codexExe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        // Default app-server transport is stdio:// JSONL.
        psi.ArgumentList.Add("app-server");

        _process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start codex app-server.");

        _ = Task.Run(() => ReadStdoutLoopAsync(_cts.Token));
        _ = Task.Run(() => ReadStderrLoopAsync(_cts.Token));

        Status?.Invoke(new StatusEvent("server", "Started codex app-server."));
        LogLine?.Invoke("Started: codex app-server");

        await InitializeAsync(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var init = await SendRequestAsync("initialize", new
        {
            clientInfo = new
            {
                name = "codex_app_server_blazor",
                title = "Codex App Server Blazor",
                version = "0.2.0"
            }
        }, cancellationToken);

        ThrowIfRpcError(init);

        await SendNotificationAsync("initialized", new { }, cancellationToken);
        Status?.Invoke(new StatusEvent("server", "Initialized Codex app-server connection."));
        LogLine?.Invoke("Initialized Codex app-server connection.");
    }

    public async Task<string> StartThreadAsync(
        string repoRoot,
        string model,
        string approvalPolicy,
        string sandbox,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("thread/start", new
        {
            model,
            cwd = repoRoot,
            approvalPolicy,
            approvalsReviewer = "user",
            sandbox,
            serviceName = "codex_app_server_blazor"
        }, cancellationToken);

        ThrowIfRpcError(response);

        var threadId = response.Result?["thread"]?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(threadId))
            throw new InvalidOperationException("thread/start succeeded but did not return thread.id. Raw: " + response.RawJson);

        ThreadId = threadId;
        Status?.Invoke(new StatusEvent("thread", $"Thread started: {threadId}"));
        LogLine?.Invoke($"Thread started: {threadId}");
        return threadId;
    }

    public async Task StartTurnAsync(
        string prompt,
        string repoRoot,
        string model,
        string approvalPolicy,
        string sandbox,
        IReadOnlyList<CodexTurnAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ThreadId))
            throw new InvalidOperationException("Start a thread first.");

        List<object> input =
        [
            new { type = "text", text = prompt }
        ];

        if (attachments is not null)
        {
            foreach (CodexTurnAttachment attachment in attachments)
            {
                input.Add(CreateTurnAttachmentInput(attachment, repoRoot));
            }
        }

        if (attachments is { Count: > 0 })
        {
            Status?.Invoke(new StatusEvent("turn/input", BuildTurnInputSummary(attachments, repoRoot)));
        }

        var response = await SendRequestAsync("turn/start", new
        {
            threadId = ThreadId,
            cwd = repoRoot,
            model,
            approvalPolicy,
            approvalsReviewer = "user",
            sandboxPolicy = CreateSandboxPolicy(sandbox),
            input
        }, cancellationToken);

        ThrowIfRpcError(response);
        Status?.Invoke(new StatusEvent("turn", "Turn accepted by app-server."));
        LogLine?.Invoke("Turn started.");
    }

    private static object CreateTurnAttachmentInput(CodexTurnAttachment attachment, string repoRoot)
    {
        return attachment.Kind switch
        {
            CodexTurnAttachmentKind.LocalImage => new
            {
                type = "localImage",
                path = attachment.Path
            },
            _ => new
            {
                type = "text",
                text = BuildAttachmentText(attachment, repoRoot)
            }
        };
    }

    private static string BuildAttachmentText(CodexTurnAttachment attachment, string repoRoot)
    {
        string submittedPath = GetSubmittedPath(attachment.Path, repoRoot);
        string content = File.ReadAllText(attachment.Path, Encoding.UTF8);
        return $$"""
        Attached file: {{attachment.Name}}
        Path: {{submittedPath}}
        Size: {{attachment.SizeBytes}} bytes

        ```text
        {{content}}
        ```
        """;
    }

    private static string BuildTurnInputSummary(IReadOnlyList<CodexTurnAttachment> attachments, string repoRoot)
    {
        List<string> parts = ["text: prompt"];
        foreach (CodexTurnAttachment attachment in attachments)
        {
            string submittedPath = attachment.Kind == CodexTurnAttachmentKind.LocalImage
                ? attachment.Path
                : GetSubmittedPath(attachment.Path, repoRoot);
            string inputType = attachment.Kind == CodexTurnAttachmentKind.LocalImage ? "localImage" : "text";
            parts.Add($"{inputType}: {attachment.Name} ({attachment.SizeBytes} bytes) -> {submittedPath}");
        }

        return "Submitting turn inputs: " + string.Join("; ", parts);
    }

    private static string GetSubmittedPath(string attachmentPath, string repoRoot)
    {
        string fullRoot = Path.GetFullPath(repoRoot);
        string fullPath = Path.GetFullPath(attachmentPath);
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            return fullPath;
        }

        return relativePath;
    }

    private static object CreateSandboxPolicy(string sandbox)
    {
        return sandbox.Trim().ToLowerInvariant() switch
        {
            "read-only" => new
            {
                type = "readOnly",
                networkAccess = false
            },
            "workspace-write" => new
            {
                type = "workspaceWrite",
                networkAccess = false,
                writableRoots = Array.Empty<string>(),
                excludeTmpdirEnvVar = false,
                excludeSlashTmp = false
            },
            "danger-full-access" => new
            {
                type = "dangerFullAccess"
            },
            _ => new
            {
                type = "readOnly",
                networkAccess = false
            }
        };
    }

    public async Task<JsonRpcResponse> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await SendRawAsync(new
        {
            method,
            id,
            @params = parameters
        }, cancellationToken);

        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetCanceled(cancellationToken);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken = default)
    {
        return SendRawAsync(new
        {
            method,
            @params = parameters
        }, cancellationToken);
    }

    public Task RespondToServerRequestAsync(
        int requestId,
        object result,
        CancellationToken cancellationToken = default)
    {
        return SendRawAsync(new
        {
            id = requestId,
            result
        }, cancellationToken);
    }

    private async Task SendRawAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        RawProtocol?.Invoke("CLIENT: " + json);
        if (rawSender is not null)
        {
            await rawSender(json, cancellationToken);
            return;
        }

        var process = _process ?? throw new InvalidOperationException("Codex app-server is not started.");
        if (process.HasExited)
            throw new InvalidOperationException("Codex app-server has exited.");

        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private async Task ReadStdoutLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var process = _process;
            if (process is null)
                return;

            while (!cancellationToken.IsCancellationRequested && !process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                RawProtocol?.Invoke("SERVER: " + line);
                HandleServerMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Status?.Invoke(new StatusEvent("error", "STDOUT reader failed: " + ex.Message));
            LogLine?.Invoke("STDOUT reader failed: " + ex.Message);
        }
    }

    private async Task ReadStderrLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var process = _process;
            if (process is null)
                return;

            while (!cancellationToken.IsCancellationRequested && !process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                RawProtocol?.Invoke("STDERR: " + line);
                Status?.Invoke(new StatusEvent("stderr", StripAnsi(line)));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            Status?.Invoke(new StatusEvent("error", "STDERR reader failed: " + ex.Message));
        }
    }

    public void HandleServerMessage(string line)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (Exception ex)
        {
            Status?.Invoke(new StatusEvent("error", "Could not parse server JSON: " + ex.Message));
            return;
        }

        if (node is null)
            return;

        var method = node["method"]?.GetValue<string>();
        var idNode = node["id"];
        if (!string.IsNullOrWhiteSpace(method)
            && idNode is not null
            && idNode.GetValueKind() == JsonValueKind.Number)
        {
            EmitServerRequest(idNode.GetValue<int>(), method, node, line);
            return;
        }

        if (idNode is not null && idNode.GetValueKind() == JsonValueKind.Number)
        {
            var id = idNode.GetValue<int>();
            var response = new JsonRpcResponse(
                Id: id,
                Result: node["result"],
                Error: node["error"],
                RawJson: line);

            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(response);

            ClassifyResponse(response);
            return;
        }

        if (!string.IsNullOrWhiteSpace(method))
            ClassifyNotification(method, node);
    }

    private void EmitServerRequest(int requestId, string method, JsonNode node, string rawJson)
    {
        string summary = BuildServerRequestSummary(method, node);
        ServerRequest?.Invoke(new CodexServerRequestEvent(
            requestId,
            method,
            summary,
            rawJson));
        Status?.Invoke(new StatusEvent(method, summary));
    }

    private static string BuildServerRequestSummary(string method, JsonNode node)
    {
        JsonNode? parameters = node["params"];
        string? reason = GetStringValue(parameters?["reason"]);
        string? cwd = GetStringValue(parameters?["cwd"]);
        string? command = GetStringValue(parameters?["command"]);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            return string.IsNullOrWhiteSpace(cwd)
                ? reason
                : $"{reason} (cwd: {cwd})";
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            return $"Approval requested for command: {command}";
        }

        return parameters is null
            ? $"Server request: {method}"
            : $"Server request: {method}: {parameters.ToJsonString(new JsonSerializerOptions { WriteIndented = false })}";
    }

    private static string? GetStringValue(JsonNode? node)
    {
        return node is not null && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : null;
    }

    private void ClassifyResponse(JsonRpcResponse response)
    {
        if (response.Error is not null)
        {
            Status?.Invoke(new StatusEvent("rpc-error", response.Error.ToJsonString()));
            return;
        }

        var result = response.Result;
        if (result?["thread"]?["id"] is JsonNode threadNode)
        {
            var threadId = threadNode.GetValue<string>();
            Status?.Invoke(new StatusEvent("thread", $"thread/start result: {threadId}"));
            return;
        }

        if (result?["turn"]?["id"] is JsonNode turnNode)
        {
            var turnId = turnNode.GetValue<string>();
            var turnStatus = result?["turn"]?["status"]?.GetValue<string>() ?? "unknown";
            Status?.Invoke(new StatusEvent("turn", $"turn/start result: {turnId} ({turnStatus})"));
        }
    }

    private void ClassifyNotification(string method, JsonNode node)
    {
        switch (method)
        {
            case "item/agentMessage/delta":
                EmitAssistantDelta(node);
                break;

            case "item/completed":
                EmitItemCompleted(node);
                break;

            case "item/started":
                EmitItemStarted(node);
                break;

            case "thread/tokenUsage/updated":
                EmitTokenTelemetry(node);
                break;

            case "account/rateLimits/updated":
                EmitRateTelemetry(node);
                break;

            case "mcpServer/startupStatus/updated":
                EmitMcpStatus(node);
                break;

            case "thread/status/changed":
            case "thread/started":
            case "turn/started":
            case "turn/completed":
            case "turn/failed":
            case "remoteControl/status/changed":
                Status?.Invoke(new StatusEvent(method, Compact(node)));
                break;

            default:
                Status?.Invoke(new StatusEvent(method, Compact(node)));
                break;
        }
    }

    private void EmitAssistantDelta(JsonNode node)
    {
        var delta = node["params"]?["delta"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(delta))
            AssistantText?.Invoke(new AssistantTextEvent(delta, IsFinal: false));
    }

    private void EmitItemStarted(JsonNode node)
    {
        var item = node["params"]?["item"];
        var type = item?["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
            return;

        switch (type)
        {
            case "commandExecution":
                ToolActivity?.Invoke(new ToolEvent(
                    "command started",
                    item?["command"]?.GetValue<string>() ?? "command",
                    item?["status"]?.GetValue<string>(),
                    item?["cwd"]?.GetValue<string>()));
                break;

            case "reasoning":
                Status?.Invoke(new StatusEvent("reasoning", "Reasoning item started."));
                break;

            case "agentMessage":
                Status?.Invoke(new StatusEvent("assistant", "Assistant response started."));
                break;

            case "userMessage":
                Status?.Invoke(new StatusEvent("context", "User/request message entered the Codex thread."));
                break;

            default:
                Status?.Invoke(new StatusEvent("item started", type));
                break;
        }
    }

    private void EmitItemCompleted(JsonNode node)
    {
        var item = node["params"]?["item"];
        var type = item?["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
            return;

        switch (type)
        {
            case "agentMessage":
                var text = item?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                    AssistantText?.Invoke(new AssistantTextEvent(text, IsFinal: true));
                Status?.Invoke(new StatusEvent("assistant", "Assistant response completed."));
                break;

            case "commandExecution":
                ToolActivity?.Invoke(new ToolEvent(
                    "command completed",
                    item?["command"]?.GetValue<string>() ?? "command",
                    item?["status"]?.GetValue<string>(),
                    item?["aggregatedOutput"]?.GetValue<string>()));
                break;

            case "reasoning":
                Status?.Invoke(new StatusEvent("reasoning", "Reasoning item completed."));
                break;

            case "userMessage":
                Status?.Invoke(new StatusEvent("context", "User/request message completed."));
                break;

            default:
                Status?.Invoke(new StatusEvent("item completed", type));
                break;
        }
    }

    private void EmitMcpStatus(JsonNode node)
    {
        var p = node["params"];
        var name = p?["name"]?.GetValue<string>() ?? "mcp";
        var status = p?["status"]?.GetValue<string>();
        var error = p?["error"]?.GetValue<string>();

        ToolActivity?.Invoke(new ToolEvent(
            "mcp status",
            name,
            status,
            error));
    }

    private void EmitTokenTelemetry(JsonNode node)
    {
        var usage = node["params"]?["tokenUsage"];
        var total = usage?["total"];

        var input = total?["inputTokens"]?.GetValue<int>();
        var cached = total?["cachedInputTokens"]?.GetValue<int>();
        var output = total?["outputTokens"]?.GetValue<int>();
        var reasoning = total?["reasoningOutputTokens"]?.GetValue<int>();
        var window = usage?["modelContextWindow"]?.GetValue<int>();

        var pct = input.HasValue && window.HasValue && window > 0
            ? $" ({(input.Value * 100.0 / window.Value):0.0}% of window)"
            : string.Empty;

        Telemetry?.Invoke(new TelemetryEvent(
            input,
            cached,
            output,
            reasoning,
            window,
            null,
            null,
            null,
            $"Tokens: input {input:N0}, cached {cached:N0}, output {output:N0}, reasoning {reasoning:N0}, window {window:N0}{pct}"));
    }

    private void EmitRateTelemetry(JsonNode node)
    {
        var rate = node["params"]?["rateLimits"];
        var primary = rate?["primary"]?["usedPercent"]?.GetValue<int>();
        var secondary = rate?["secondary"]?["usedPercent"]?.GetValue<int>();
        var plan = rate?["planType"]?.GetValue<string>();

        Telemetry?.Invoke(new TelemetryEvent(
            null,
            null,
            null,
            null,
            null,
            primary,
            secondary,
            plan,
            $"Rate limits: primary {primary}%, secondary {secondary}%, plan {plan}"));
    }

    private static string Compact(JsonNode node)
    {
        try
        {
            var method = node["method"]?.GetValue<string>() ?? "event";
            var p = node["params"];

            return p is null
                ? method
                : method + ": " + p.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return node.ToJsonString();
        }
    }

    private static string StripAnsi(string value)
    {
        // Good enough for the colored tracing lines emitted by Codex stderr.
        var sb = new StringBuilder(value.Length);
        var inEscape = false;
        foreach (var ch in value)
        {
            if (ch == '\u001b')
            {
                inEscape = true;
                continue;
            }

            if (inEscape)
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                    inEscape = false;
                continue;
            }

            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void ThrowIfRpcError(JsonRpcResponse response)
    {
        if (response.Error is not null)
            throw new InvalidOperationException("Codex app-server error: " + response.Error.ToJsonString());
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_process is { HasExited: false } process)
        {
            try
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(1500))
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore shutdown errors.
            }
        }

        _process?.Dispose();
        _cts.Dispose();
        await Task.CompletedTask;
    }
}
