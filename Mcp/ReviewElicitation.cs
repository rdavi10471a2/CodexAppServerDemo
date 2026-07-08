using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

/// <summary>
/// Outcome of a governed staged-review decision requested from the human operator.
/// </summary>
public enum ReviewDecision
{
    Accepted,
    Rejected,
    Cancelled
}

/// <summary>
/// Context for a single governed staged-review decision surfaced to the operator.
/// </summary>
public sealed record ReviewElicitationRequest(
    string RelativePath,
    string SessionLabel,
    int PendingCount,
    bool ValidationIsError,
    string ValidationStatus,
    int ValidationDiagnosticCount,
    string ReviewUrl);

/// <summary>
/// Requests a governed staged-review accept/reject decision from the operator and blocks until it is made.
/// Implemented over MCP elicitation so the request travels the same app-server server-request channel that
/// security/sandbox approvals already use, which lets the agent turn suspend until the operator answers.
/// </summary>
public interface IReviewElicitor
{
    Task<ReviewDecision> RequestDecisionAsync(ReviewElicitationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Real elicitor backed by <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>.
/// This is a thin adapter over the SDK call; the decision-mapping logic it feeds lives in the review service
/// so it can be unit tested with a stub elicitor.
/// </summary>
public sealed class McpServerReviewElicitor : IReviewElicitor
{
    private readonly McpServer server;

    public McpServerReviewElicitor(McpServer server)
    {
        this.server = server;
    }

    public async Task<ReviewDecision> RequestDecisionAsync(
        ReviewElicitationRequest request,
        CancellationToken cancellationToken)
    {
        string validationLine = request.ValidationIsError
            ? $"WARNING: pre-merge validation reported {request.ValidationDiagnosticCount} issue(s) (status: {request.ValidationStatus}). Accepting overrides the failed validation."
            : "Pre-merge validation passed.";
        string message =
            $"Governed review for '{request.RelativePath}' in {request.SessionLabel}."
            + $" {validationLine}"
            + $" Pending in session: {request.PendingCount}."
            + " Accept applies the staged change to watched source; decline rejects it and leaves source unchanged."
            + $" Review detail: {request.ReviewUrl}";

        ElicitResult result = await server.ElicitAsync(
            new ElicitRequestParams
            {
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["approve"] = new ElicitRequestParams.BooleanSchema
                        {
                            Title = "Accept staged change",
                            Description = "Accept applies the staged edit to watched source; decline rejects it."
                        }
                    }
                }
            },
            cancellationToken);

        if (result.IsAccepted)
        {
            return ReviewDecision.Accepted;
        }

        if (string.Equals(result.Action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewDecision.Cancelled;
        }

        return ReviewDecision.Rejected;
    }
}
