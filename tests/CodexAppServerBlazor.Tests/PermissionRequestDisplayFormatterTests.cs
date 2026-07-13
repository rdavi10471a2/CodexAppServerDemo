using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class PermissionRequestDisplayFormatterTests
{
    [Fact]
    public void Build_formats_mcp_tool_call_with_named_arguments()
    {
        CodexPermissionRequest request = new(
            RequestId: 7,
            Method: "mcpServer/elicitation/request",
            Summary: "Approve governed refresh",
            RawJson:
            """
            {"id":7,"method":"mcpServer/elicitation/request","params":{"server":"harness","tool":"refresh_file","arguments":{"watchedFilePath":"C:\\Work\\Models\\HelpSubject.cs","sessionId":"edit-123","containingType":"HelpSubject"},"prompt":"Refresh the governed working copy before editing."}}
            """,
            CorrelationId: null,
            ApprovalId: null,
            SupportsSessionApproval: false,
            SupportsPersistentApproval: false,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            ResolvedAt: null);

        PermissionRequestDisplayModel display = PermissionRequestDisplayFormatter.Build(request);

        Assert.Equal("MCP Tool Call", display.KindLabel);
        Assert.Equal("Approve MCP tool call", display.Headline);
        Assert.Equal("Prompt", display.DetailTitle);
        Assert.Contains("Refresh the governed working copy", display.DetailText, StringComparison.Ordinal);
        Assert.Contains(display.Fields, field => field.Label == "Watched File Path" && field.Value.Contains("HelpSubject.cs", StringComparison.Ordinal));
        Assert.Contains(display.Fields, field => field.Label == "Session Id" && field.Value == "edit-123");
        Assert.Contains("\"containingType\": \"HelpSubject\"", display.ArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_formats_command_approval_from_legacy_exec_shape()
    {
        CodexPermissionRequest request = new(
            RequestId: 99,
            Method: "execCommandApproval",
            Summary: "Approval requested for command execution",
            RawJson:
            """
            {"id":99,"method":"execCommandApproval","params":{"callId":"call-1","command":["powershell","-NoProfile","-Command","Get-Date"],"cwd":"C:\\Work"}}
            """,
            CorrelationId: "call-1",
            ApprovalId: null,
            SupportsSessionApproval: true,
            SupportsPersistentApproval: false,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            ResolvedAt: null);

        PermissionRequestDisplayModel display = PermissionRequestDisplayFormatter.Build(request);

        Assert.Equal("Command Approval", display.KindLabel);
        Assert.Equal("Run command", display.Headline);
        Assert.Equal("Command", display.DetailTitle);
        Assert.Contains("powershell -NoProfile -Command Get-Date", display.DetailText, StringComparison.Ordinal);
        Assert.Contains(display.Fields, field => field.Label == "CWD" && field.Value == "C:\\Work");
        Assert.Contains(display.Fields, field => field.Label == "Call Id" && field.Value == "call-1");
    }

    [Fact]
    public void Build_formats_permission_request_with_requested_permissions()
    {
        CodexPermissionRequest request = new(
            RequestId: 42,
            Method: "item/permissions/requestApproval",
            Summary: "Need network (cwd: C:\\Work)",
            RawJson:
            """
            {"id":42,"method":"item/permissions/requestApproval","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"item-1","cwd":"C:\\Work","startedAtMs":1000,"reason":"Need network","permissions":{"network":{"enabled":true},"fileSystem":{"enabled":false}}}}
            """,
            CorrelationId: "item-1",
            ApprovalId: null,
            SupportsSessionApproval: true,
            SupportsPersistentApproval: false,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            ResolvedAt: null);

        PermissionRequestDisplayModel display = PermissionRequestDisplayFormatter.Build(request);

        Assert.Equal("Permission Request", display.KindLabel);
        Assert.Equal("Grant permissions", display.Headline);
        Assert.Contains(display.Fields, field => field.Label == "Reason" && field.Value == "Need network");
        Assert.Contains(display.Fields, field => field.Label == "Requested Permissions" && field.Value.Contains("network (enabled)", StringComparison.Ordinal));
        Assert.Contains(display.Fields, field => field.Label == "Requested Permissions" && field.Value.Contains("fileSystem (disabled)", StringComparison.Ordinal));
        Assert.Contains("\"network\": {", display.ArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_formats_wrapped_mcp_tool_approval_from_elicitation_metadata()
    {
        CodexPermissionRequest request = new(
            RequestId: 0,
            Method: "mcpServer/elicitation/request",
            Summary: "Need approval",
            RawJson:
            """
            {
              "id": 0,
              "method": "mcpServer/elicitation/request",
              "params": {
                "threadId": "thread-1",
                "turnId": "turn-1",
                "serverName": "harness",
                "mode": "form",
                "_meta": {
                  "codex_approval_kind": "mcp_tool_call",
                  "persist": ["session", "always"],
                  "tool_description": "Returns the current Active task for the selected workspace.",
                  "tool_params": {},
                  "tool_params_display": []
                },
                "message": "Allow the harness MCP server to run tool \"get_current_task\"?",
                "requestedSchema": {
                  "type": "object",
                  "properties": {
                    "answer": {
                      "type": "boolean",
                      "title": "Yes / No",
                      "default": false
                    }
                  },
                  "required": ["answer"]
                }
              }
            }
            """,
            CorrelationId: null,
            ApprovalId: null,
            SupportsSessionApproval: false,
            SupportsPersistentApproval: false,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            ResolvedAt: null);

        PermissionRequestDisplayModel display = PermissionRequestDisplayFormatter.Build(request);

        Assert.Equal("MCP Tool Call", display.KindLabel);
        Assert.Equal("Approve MCP tool call", display.Headline);
        Assert.Contains("get_current_task", display.Summary, StringComparison.Ordinal);
        Assert.Contains(display.Fields, field => field.Label == "Server" && field.Value == "harness");
        Assert.Contains(display.Fields, field => field.Label == "Tool" && field.Value == "get_current_task");
        Assert.DoesNotContain(display.Fields, field => field.Label == "Persistence");
        Assert.Equal("Approval details", display.ArgumentsTitle);
        Assert.Contains("approvalScope", display.ArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_formats_wrapped_operator_confirmation_as_agent_question()
    {
        CodexPermissionRequest request = new(
            RequestId: 8,
            Method: "mcpServer/elicitation/request",
            Summary: "Need guidance",
            RawJson:
            """
            {
              "id": 8,
              "method": "mcpServer/elicitation/request",
              "params": {
                "serverName": "harness",
                "mode": "form",
                "message": "Do you want the results of TASK-0009 pushed to the task markdown notes files?",
                "_meta": {
                  "codex_approval_kind": "mcp_tool_call",
                  "tool_name": "request_operator_confirmation",
                  "tool_description": "Requests a strict yes/no answer from the operator through MCP elicitation."
                },
                "requestedSchema": {
                  "type": "object",
                  "properties": {
                    "answer": {
                      "type": "boolean",
                      "title": "Yes / No",
                      "description": "True means yes. False means no.",
                      "default": false
                    }
                  },
                  "required": ["answer"]
                }
              }
            }
            """,
            CorrelationId: null,
            ApprovalId: null,
            SupportsSessionApproval: false,
            SupportsPersistentApproval: false,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            ResolvedAt: null);

        PermissionRequestDisplayModel display = PermissionRequestDisplayFormatter.Build(request);

        Assert.Equal("Agent Question", display.KindLabel);
        Assert.Equal("Answer agent question", display.Headline);
        Assert.Equal("Prompt", display.DetailTitle);
        Assert.Contains("TASK-0009", display.DetailText, StringComparison.Ordinal);
        Assert.Equal("Choose yes or no to continue the governed workflow.", display.Summary);
        Assert.Contains(display.Fields, field => field.Label == "Answer Type" && field.Value == "Yes / No");
        Assert.Contains(display.Fields, field => field.Label == "Default" && field.Value == "No");
        Assert.Null(display.ArgumentsJson);
    }
}
