using System.Text.Json.Nodes;
using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class CodexAppServerPermissionRequestTests
{
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
    public async Task RespondToServerRequestAsync_writes_json_rpc_empty_permission_grant()
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
}
