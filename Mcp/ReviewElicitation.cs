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
/// Context for a governed staged-review decision surfaced to the operator.
/// </summary>
public sealed record ReviewElicitationRequest(
    string SessionId,
    string RelativePath,
    string SessionLabel,
    int PendingCount,
    bool ValidationIsError,
    string ValidationStatus,
    int ValidationDiagnosticCount,
    string ReviewUrl);

/// <summary>
/// Correlation marker embedded in the elicitation message so the Blazor host can recognize a governed
/// review elicitation and route it into the session review dialog instead of the generic approval panel.
/// The app-server elicitation protocol has no per-session field, so the session id rides in the message.
/// </summary>
public static class ReviewElicitationMarker
{
    private const string Prefix = "[[aim-review:";
    private const string Suffix = "]]";

    public static string Build(string sessionId)
    {
        return $"{Prefix}{sessionId}{Suffix}";
    }

    public static bool TryParse(string? text, out string sessionId)
    {
        sessionId = string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += Prefix.Length;
        int end = text.IndexOf(Suffix, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        sessionId = text[start..end];
        return !string.IsNullOrWhiteSpace(sessionId);
    }
}

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
            ? $"WARNING: pre-merge validation reported {request.ValidationDiagnosticCount} issue(s) (status: {request.ValidationStatus})."
            : "Pre-merge validation passed.";
        string message =
            $"Governed review ready for {request.SessionLabel} ('{request.RelativePath}', {request.PendingCount} pending)."
            + $" {validationLine}"
            + " Approve to open the review dialog and resolve every staged file in this edit session;"
            + " the agent stays blocked until the session is fully reviewed or the dialog is closed."
            + $" Review detail: {request.ReviewUrl} {ReviewElicitationMarker.Build(request.SessionId)}";

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
