using System.Text.Json;
using System.Text.Json.Nodes;
using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class CodexAppServerPermissionRequestTests
{
    [Fact]
    public async Task StartTurnAsync_sends_policy_reviewer_and_sandbox_policy()
    {
        List<string> sentMessages = [];
        CodexAppServerClient? client = null;
        client = new CodexAppServerClient((json, _) =>
        {
            sentMessages.Add(json);
            JsonNode? request = JsonNode.Parse(json);
            int id = request?["id"]?.GetValue<int>() ?? 0;
            string? method = request?["method"]?.GetValue<string>();
            if (id > 0)
            {
                object result = method == "thread/start"
                    ? new
                    {
                        thread = new
                        {
                            id = "thread-1"
                        }
                    }
                    : new
                    {
                        turn = new
                        {
                            id = "turn-1",
                            status = "inProgress"
                        }
                    };

                client!.HandleServerMessage(JsonSerializer.Serialize(new
                {
                    id,
                    result
                }));
            }

            return Task.CompletedTask;
        });

        await client.StartThreadAsync(
            "C:\\Work",
            "gpt-5.4",
            "on-request",
            "read-only");

        await client.StartTurnAsync(
            "search please",
            "C:\\Work",
            "gpt-5.4",
            "on-request",
            "read-only");

        JsonNode? threadStart = null;
        JsonNode? turnStart = null;
        foreach (string messageJson in sentMessages)
        {
            JsonNode? message = JsonNode.Parse(messageJson);
            if (message?["method"]?.GetValue<string>() == "thread/start")
            {
                threadStart = message;
            }

            if (message?["method"]?.GetValue<string>() == "turn/start")
            {
                turnStart = message;
            }
        }
        JsonNode? threadParameters = threadStart?["params"];
        JsonNode? parameters = turnStart?["params"];

        Assert.NotNull(threadStart);
        Assert.NotNull(turnStart);
        Assert.True(threadParameters?["approvalPolicy"]?["granular"]?["sandbox_approval"]?.GetValue<bool>());
        Assert.False(threadParameters?["approvalPolicy"]?["granular"]?["mcp_elicitations"]?.GetValue<bool>());
        Assert.False(threadParameters?["approvalPolicy"]?["granular"]?["rules"]?.GetValue<bool>());
        Assert.Null(threadParameters?["approvalPolicy"]?["granular"]?["request_permissions"]);
        Assert.Null(threadParameters?["approvalPolicy"]?["granular"]?["skill_approval"]);
        Assert.Equal("thread-1", parameters?["threadId"]?.GetValue<string>());
        Assert.Equal("C:\\Work", parameters?["cwd"]?.GetValue<string>());
        Assert.Equal("gpt-5.4", parameters?["model"]?.GetValue<string>());
        Assert.True(parameters?["approvalPolicy"]?["granular"]?["sandbox_approval"]?.GetValue<bool>());
        Assert.False(parameters?["approvalPolicy"]?["granular"]?["mcp_elicitations"]?.GetValue<bool>());
        Assert.False(parameters?["approvalPolicy"]?["granular"]?["rules"]?.GetValue<bool>());
        Assert.Null(parameters?["approvalPolicy"]?["granular"]?["request_permissions"]);
        Assert.Null(parameters?["approvalPolicy"]?["granular"]?["skill_approval"]);
        Assert.Equal("user", parameters?["approvalsReviewer"]?.GetValue<string>());
        Assert.Equal("readOnly", parameters?["sandboxPolicy"]?["type"]?.GetValue<string>());
        Assert.False(parameters?["sandboxPolicy"]?["networkAccess"]?.GetValue<bool>());
    }

    [Fact]
    public async Task StartTurnAsync_workspace_write_includes_repo_root_in_writable_roots()
    {
        const string repoRoot = "C:\\Work";
        List<string> sentMessages = [];
        CodexAppServerClient? client = null;
        client = new CodexAppServerClient((json, _) =>
        {
            sentMessages.Add(json);
            JsonNode? request = JsonNode.Parse(json);
            int id = request?["id"]?.GetValue<int>() ?? 0;
            string? method = request?["method"]?.GetValue<string>();
            if (id > 0)
            {
                object result = method == "thread/start"
                    ? new
                    {
                        thread = new
                        {
                            id = "thread-1"
                        }
                    }
                    : new
                    {
                        turn = new
                        {
                            id = "turn-1",
                            status = "inProgress"
                        }
                    };

                client!.HandleServerMessage(JsonSerializer.Serialize(new
                {
                    id,
                    result
                }));
            }

            return Task.CompletedTask;
        });

        await client.StartThreadAsync(
            repoRoot,
            "gpt-5.4",
            "on-request",
            "workspace-write");

        await client.StartTurnAsync(
            "search please",
            repoRoot,
            "gpt-5.4",
            "on-request",
            "workspace-write");

        JsonNode? turnStart = sentMessages
            .Select(messageJson => JsonNode.Parse(messageJson))
            .FirstOrDefault(message => message?["method"]?.GetValue<string>() == "turn/start");
        JsonNode? parameters = turnStart?["params"];
        JsonArray? writableRoots = parameters?["sandboxPolicy"]?["writableRoots"]?.AsArray();

        Assert.NotNull(turnStart);
        Assert.Equal("workspaceWrite", parameters?["sandboxPolicy"]?["type"]?.GetValue<string>());
        Assert.False(parameters?["sandboxPolicy"]?["networkAccess"]?.GetValue<bool>());
        Assert.NotNull(writableRoots);
        Assert.Single(writableRoots!);
        Assert.Equal(repoRoot, writableRoots[0]?.GetValue<string>());
    }

    [Fact]
    public async Task StartTurnAsync_sends_text_images_and_text_file_contents()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "CodexAppServerTests", Guid.NewGuid().ToString("N"));
        string attachmentRoot = Path.Combine(repoRoot, "runtime", "turn-attachments");
        Directory.CreateDirectory(attachmentRoot);
        string imagePath = Path.Combine(attachmentRoot, "screen.png");
        string notesPath = Path.Combine(attachmentRoot, "notes.md");
        await File.WriteAllTextAsync(notesPath, "hello from notes");

        List<string> sentMessages = [];
        CodexAppServerClient? client = null;
        client = new CodexAppServerClient((json, _) =>
        {
            sentMessages.Add(json);
            JsonNode? request = JsonNode.Parse(json);
            int id = request?["id"]?.GetValue<int>() ?? 0;
            string? method = request?["method"]?.GetValue<string>();
            if (id > 0)
            {
                object result = method == "thread/start"
                    ? new
                    {
                        thread = new
                        {
                            id = "thread-1"
                        }
                    }
                    : new
                    {
                        turn = new
                        {
                            id = "turn-1",
                            status = "inProgress"
                        }
                    };

                client!.HandleServerMessage(JsonSerializer.Serialize(new
                {
                    id,
                    result
                }));
            }

            return Task.CompletedTask;
        });

        try
        {
            await client.StartThreadAsync(
                repoRoot,
                "gpt-5.4",
                "on-request",
                "read-only");

            await client.StartTurnAsync(
                "review these",
                repoRoot,
                "gpt-5.4",
                "on-request",
                "read-only",
                [
                    new CodexTurnAttachment("screen.png", imagePath, CodexTurnAttachmentKind.LocalImage, 1024),
                    new CodexTurnAttachment("notes.md", notesPath, CodexTurnAttachmentKind.Text, 2048)
                ]);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }

        JsonNode? turnStart = sentMessages
            .Select(messageJson => JsonNode.Parse(messageJson))
            .FirstOrDefault(message => message?["method"]?.GetValue<string>() == "turn/start");
        JsonArray? input = turnStart?["params"]?["input"]?.AsArray();

        Assert.NotNull(input);
        Assert.Equal(3, input!.Count);
        Assert.Equal("text", input[0]?["type"]?.GetValue<string>());
        Assert.Equal("review these", input[0]?["text"]?.GetValue<string>());
        Assert.Equal("localImage", input[1]?["type"]?.GetValue<string>());
        Assert.Equal(imagePath, input[1]?["path"]?.GetValue<string>());
        Assert.Equal("text", input[2]?["type"]?.GetValue<string>());
        Assert.Contains("Attached file: notes.md", input[2]?["text"]?.GetValue<string>());
        Assert.Contains("Path: runtime\\turn-attachments\\notes.md", input[2]?["text"]?.GetValue<string>());
        Assert.Contains("hello from notes", input[2]?["text"]?.GetValue<string>());
    }

    [Fact]
    public void HandleServerMessage_emits_server_request_for_permission_approval()
    {
        CodexAppServerClient client = new();
        CodexServerRequestEvent? observedRequest = null;
        StatusEvent? observedStatus = null;
        client.ServerRequest += request => observedRequest = request;
        client.Status += status => observedStatus = status;

        client.HandleServerMessage("""
            {"id":42,"method":"item/permissions/requestApproval","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"item-1","cwd":"C:\\Work","startedAtMs":1000,"reason":"Need network","permissions":{"network":{"enabled":true}}}}
            """);

        Assert.NotNull(observedRequest);
        Assert.Equal(42, observedRequest.RequestId);
        Assert.Equal("item/permissions/requestApproval", observedRequest.Method);
        Assert.Equal("item-1", observedRequest.CorrelationId);
        Assert.Contains("Need network", observedRequest.Summary);
        Assert.Contains("C:\\Work", observedRequest.Summary);
        Assert.NotNull(observedStatus);
        Assert.Equal("item/permissions/requestApproval", observedStatus.EventType);
    }

    [Fact]
    public void HandleServerMessage_emits_server_request_for_legacy_exec_approval()
    {
        CodexAppServerClient client = new();
        CodexServerRequestEvent? observedRequest = null;
        client.ServerRequest += request => observedRequest = request;

        client.HandleServerMessage("""
            {"id":99,"method":"execCommandApproval","params":{"callId":"call-1","command":["powershell","-NoProfile","-Command","Get-Date"],"cwd":"C:\\Work"}}
            """);

        Assert.NotNull(observedRequest);
        Assert.Equal(99, observedRequest.RequestId);
        Assert.Equal("execCommandApproval", observedRequest.Method);
        Assert.Equal("call-1", observedRequest.CorrelationId);
        Assert.Contains("execCommandApproval", observedRequest.Summary);
    }

    [Fact]
    public void HandleServerMessage_emits_command_completion_with_correlation_id()
    {
        CodexAppServerClient client = new();
        ToolEvent? observedEvent = null;
        client.ToolActivity += toolEvent => observedEvent = toolEvent;

        client.HandleServerMessage("""
            {"method":"item/completed","params":{"item":{"type":"commandExecution","id":"cmd-1","command":"dotnet test","status":"completed","cwd":"C:\\Work","exitCode":0,"durationMs":42,"processId":"pty-1","aggregatedOutput":"ok"}}}
            """);

        Assert.NotNull(observedEvent);
        Assert.Equal("command completed", observedEvent!.EventType);
        Assert.Equal("cmd-1", observedEvent.CorrelationId);
        Assert.Contains("cwd: C:\\Work", observedEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("exit: 0", observedEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("duration: 42 ms", observedEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("pid: pty-1", observedEvent.Detail, StringComparison.Ordinal);
        Assert.Contains("ok", observedEvent.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleServerMessage_emits_file_change_completion_with_correlation_id()
    {
        CodexAppServerClient client = new();
        ToolEvent? observedEvent = null;
        client.ToolActivity += toolEvent => observedEvent = toolEvent;

        client.HandleServerMessage("""
            {"method":"item/completed","params":{"item":{"type":"fileChange","id":"patch-1","status":"completed","changes":[]}}}
            """);

        Assert.NotNull(observedEvent);
        Assert.Equal("file change completed", observedEvent!.EventType);
        Assert.Equal("patch-1", observedEvent.CorrelationId);
    }

    [Fact]
    public void HandleServerMessage_emits_assistant_text_from_completed_content_blocks()
    {
        CodexAppServerClient client = new();
        List<AssistantTextEvent> assistantEvents = [];
        client.AssistantText += assistantEvents.Add;

        client.HandleServerMessage("""
            {"method":"item/completed","params":{"item":{"type":"agentMessage","content":[{"type":"output_text","text":"hello "},{"type":"text","text":"world"}]}}}
            """);

        AssistantTextEvent observed = Assert.Single(assistantEvents);
        Assert.True(observed.IsFinal);
        Assert.Equal("hello world", observed.Text);
    }

    [Fact]
    public void HandleServerMessage_emits_assistant_delta_from_content_block_delta()
    {
        CodexAppServerClient client = new();
        List<AssistantTextEvent> assistantEvents = [];
        client.AssistantText += assistantEvents.Add;

        client.HandleServerMessage("""
            {"method":"item/agentMessage/delta","params":{"delta":{"type":"output_text","text":"streamed text"}}}
            """);

        AssistantTextEvent observed = Assert.Single(assistantEvents);
        Assert.False(observed.IsFinal);
        Assert.Equal("streamed text", observed.Text);
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_json_rpc_denial_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateDenyResponse(
            "item/commandExecution/requestApproval",
            cancelTurn: false);
        await client.RespondToServerRequestAsync(42, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(42, message?["id"]?.GetValue<int>());
        Assert.Equal("decline", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_command_approval_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateApproveResponse(
            "item/commandExecution/requestApproval",
            "{}",
            PermissionApprovalScope.Turn);
        await client.RespondToServerRequestAsync(43, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(43, message?["id"]?.GetValue<int>());
        Assert.Equal("accept", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_command_session_approval_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateApproveResponse(
            "item/commandExecution/requestApproval",
            "{}",
            PermissionApprovalScope.Session);
        await client.RespondToServerRequestAsync(44, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(44, message?["id"]?.GetValue<int>());
        Assert.Equal("acceptForSession", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_command_persistent_approval_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateApproveResponse(
            "item/commandExecution/requestApproval",
            """
            {"params":{"proposedExecpolicyAmendment":["allowed-prefix"]}}
            """,
            PermissionApprovalScope.Persistent);
        await client.RespondToServerRequestAsync(45, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        JsonNode? amendment = message?["result"]?["decision"]?["acceptWithExecpolicyAmendment"]?["execpolicy_amendment"];
        Assert.Equal(45, message?["id"]?.GetValue<int>());
        Assert.Equal("allowed-prefix", amendment?[0]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_file_change_session_approval_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateApproveResponse(
            "item/fileChange/requestApproval",
            "{}",
            PermissionApprovalScope.Session);
        await client.RespondToServerRequestAsync(46, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(46, message?["id"]?.GetValue<int>());
        Assert.Equal("acceptForSession", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_legacy_exec_denial_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateDenyResponse(
            "execCommandApproval",
            cancelTurn: false);
        await client.RespondToServerRequestAsync(99, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(99, message?["id"]?.GetValue<int>());
        Assert.Equal("denied", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_legacy_patch_cancel_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateDenyResponse(
            "applyPatchApproval",
            cancelTurn: true);
        await client.RespondToServerRequestAsync(100, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(100, message?["id"]?.GetValue<int>());
        Assert.Equal("abort", message?["result"]?["decision"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_elicitation_decline_response()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateDenyResponse(
            "mcpServer/elicitation/request",
            cancelTurn: false);
        await client.RespondToServerRequestAsync(101, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        Assert.Equal(101, message?["id"]?.GetValue<int>());
        Assert.Equal("decline", message?["result"]?["action"]?.GetValue<string>());
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_json_rpc_permission_denial()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateDenyResponse(
            "item/permissions/requestApproval",
            cancelTurn: false);
        await client.RespondToServerRequestAsync(7, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        JsonNode? result = message?["result"];
        Assert.Equal(7, message?["id"]?.GetValue<int>());
        Assert.Equal("turn", result?["scope"]?.GetValue<string>());
        Assert.True(result?["strictAutoReview"]?.GetValue<bool>());
        Assert.Null(result?["permissions"]?["fileSystem"]);
        Assert.Null(result?["permissions"]?["network"]);
    }

    [Fact]
    public async Task RespondToServerRequestAsync_writes_json_rpc_permission_grant()
    {
        List<string> sentMessages = [];
        CodexAppServerClient client = new((json, _) =>
        {
            sentMessages.Add(json);
            return Task.CompletedTask;
        });

        object response = PermissionRequestService.CreateApproveResponse(
            "item/permissions/requestApproval",
            """
            {"params":{"permissions":{"network":{"enabled":true}}}}
            """,
            PermissionApprovalScope.Session);
        await client.RespondToServerRequestAsync(8, response);

        JsonNode? message = JsonNode.Parse(Assert.Single(sentMessages));
        JsonNode? result = message?["result"];
        Assert.Equal(8, message?["id"]?.GetValue<int>());
        Assert.Equal("session", result?["scope"]?.GetValue<string>());
        Assert.True(result?["strictAutoReview"]?.GetValue<bool>());
        Assert.True(result?["permissions"]?["network"]?["enabled"]?.GetValue<bool>());
    }

    [Fact]
    public void ResolveCommandResult_matches_exact_correlation_id()
    {
        PermissionRequestService service = new();
        service.Add(new CodexServerRequestEvent(
            1,
            "item/commandExecution/requestApproval",
            "first",
            "{}",
            "cmd-1",
            null));
        service.Add(new CodexServerRequestEvent(
            2,
            "item/commandExecution/requestApproval",
            "second",
            "{}",
            "cmd-2",
            null));

        Assert.NotNull(service.Resolve(1, "approved"));
        Assert.NotNull(service.Resolve(2, "approved for session"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult("cmd-2", null, "command completed");

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }

    [Fact]
    public void ResolveCommandResult_falls_back_when_correlation_id_is_missing()
    {
        PermissionRequestService service = new();
        service.Add(new CodexServerRequestEvent(
            1,
            "mcpServer/elicitation/request",
            "need guidance",
            "{}",
            null,
            null));
        service.Add(new CodexServerRequestEvent(
            2,
            "item/commandExecution/requestApproval",
            "run command",
            "{}",
            "cmd-2",
            null));

        Assert.NotNull(service.Resolve(1, "approved"));
        Assert.NotNull(service.Resolve(2, "approved"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult(null, null, "command completed");

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }

    [Fact]
    public void ResolveCommandResult_prefers_approval_id_when_present()
    {
        PermissionRequestService service = new();
        service.Add(new CodexServerRequestEvent(
            1,
            "item/commandExecution/requestApproval",
            "first callback",
            "{}",
            "cmd-parent",
            "approval-1"));
        service.Add(new CodexServerRequestEvent(
            2,
            "item/commandExecution/requestApproval",
            "second callback",
            "{}",
            "cmd-parent",
            "approval-2"));

        Assert.NotNull(service.Resolve(1, "approved"));
        Assert.NotNull(service.Resolve(2, "approved"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult("cmd-parent", "approval-2", "command completed");

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().OrderBy(r => r.RequestId).ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }
}
