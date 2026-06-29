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

        JsonNode? turnStart = null;
        foreach (string messageJson in sentMessages)
        {
            JsonNode? message = JsonNode.Parse(messageJson);
            if (message?["method"]?.GetValue<string>() == "turn/start")
            {
                turnStart = message;
                break;
            }
        }
        JsonNode? parameters = turnStart?["params"];

        Assert.NotNull(turnStart);
        Assert.Equal("thread-1", parameters?["threadId"]?.GetValue<string>());
        Assert.Equal("C:\\Work", parameters?["cwd"]?.GetValue<string>());
        Assert.Equal("gpt-5.4", parameters?["model"]?.GetValue<string>());
        Assert.Equal("on-request", parameters?["approvalPolicy"]?.GetValue<string>());
        Assert.Equal("user", parameters?["approvalsReviewer"]?.GetValue<string>());
        Assert.Equal("readOnly", parameters?["sandboxPolicy"]?["type"]?.GetValue<string>());
        Assert.False(parameters?["sandboxPolicy"]?["networkAccess"]?.GetValue<bool>());
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
        Assert.Contains("execCommandApproval", observedRequest.Summary);
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
    public void ResolveLatestApproved_updates_only_latest_approved_request()
    {
        PermissionRequestService service = new();
        service.Add(new CodexServerRequestEvent(
            1,
            "item/commandExecution/requestApproval",
            "first",
            "{}"));
        service.Add(new CodexServerRequestEvent(
            2,
            "item/commandExecution/requestApproval",
            "second",
            "{}"));

        Assert.NotNull(service.Resolve(1, "approved"));
        Assert.NotNull(service.Resolve(2, "approved for session"));

        CodexPermissionRequest? resolved = service.ResolveLatestApproved("command completed");

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }
}
