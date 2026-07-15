using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

[McpServerToolType]
public sealed class ElicitationProbeMcpTools
{
    [McpServerTool]
    [Description("Requests a strict yes/no decision from the operator through MCP elicitation and returns the action plus the boolean answer. Use this for bounded governed operator decisions, not just confirmations.")]
    public static async Task<object> RequestOperatorDecision(
        McpServer server,
        string question,
        string? detail,
        bool defaultAnswer,
        CancellationToken cancellationToken)
    {
        if (server.ClientCapabilities?.Elicitation is null)
        {
            throw new InvalidOperationException("Client does not advertise MCP elicitation support.");
        }

        ElicitResult result = await server.ElicitAsync(
            new ElicitRequestParams
            {
                Message = string.IsNullOrWhiteSpace(detail)
                    ? question
                    : $"{question} {detail}",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["answer"] = new ElicitRequestParams.BooleanSchema
                        {
                            Title = "Yes / No",
                            Description = "True means yes. False means no.",
                            Default = defaultAnswer
                        }
                    },
                    Required = ["answer"]
                }
            },
            cancellationToken);

        bool? answer = null;
        if (result.IsAccepted
            && result.Content is not null
            && result.Content.TryGetValue("answer", out JsonElement answerElement)
            && answerElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            answer = answerElement.GetBoolean();
        }

        return new
        {
            action = result.Action,
            answer
        };
    }

    [McpServerTool]
    [Description("Raises a simple yes/no operator-decision elicitation so the host can test schema-driven governed questions.")]
    public static async Task<object> ProbeOperatorDecisionElicitation(McpServer server, CancellationToken cancellationToken)
    {
        return await RequestOperatorDecision(
            server,
            "Should I update agent notes for this task outcome?",
            "Choose yes only when durable workflow memory changed.",
            defaultAnswer: false,
            cancellationToken);
    }
}
