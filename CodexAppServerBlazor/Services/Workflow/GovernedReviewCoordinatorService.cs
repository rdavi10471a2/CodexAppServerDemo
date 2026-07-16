namespace CodexAppServerBlazor.Services.Workflow;

// Bridges the governed review dialog to the blocking primitive. The blocking primitive is now the MCP
// elicitation (raised by the MCP tool over the app-server stdio channel), NOT this coordinator. When that
// elicitation arrives, CodexConnectionService calls QueueAndWaitAsync here to drive the existing Home.razor.cs
// dialog flow; Home resolves every staged file in the session and calls Complete when the dialog drains or
// closes; CodexConnectionService then answers the elicitation, unblocking the agent turn. So this coordinator
// no longer blocks the agent (the elicitation does) -- it only sequences the host dialog and reports the
// drain/close result back to the elicitation answerer.
public sealed class GovernedReviewCoordinatorService
{
    private readonly object gate = new();
    private GovernedReviewPendingRequest? pendingRequest;

    public event Action? Changed;

    public GovernedReviewPendingRequest? GetPendingRequest()
    {
        lock (gate)
        {
            return pendingRequest;
        }
    }

    public bool IsSessionPending(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        lock (gate)
        {
            return pendingRequest is not null
                && pendingRequest.Request.SessionId.Equals(sessionId, StringComparison.Ordinal);
        }
    }

    public Task<GovernedReviewResolution> QueueAndWaitAsync(
        GovernedReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(request));
        }

        GovernedReviewPendingRequest activeRequest;
        bool notifyChanged = false;
        lock (gate)
        {
            if (pendingRequest is not null)
            {
                if (pendingRequest.Request.SessionId.Equals(request.SessionId, StringComparison.Ordinal))
                {
                    return pendingRequest.Completion.Task;
                }

                throw new InvalidOperationException(
                    $"Governed review session '{pendingRequest.Request.SessionId}' is already awaiting a host decision.");
            }

            activeRequest = new GovernedReviewPendingRequest(
                request,
                new TaskCompletionSource<GovernedReviewResolution>(TaskCreationOptions.RunContinuationsAsynchronously));
            pendingRequest = activeRequest;
            notifyChanged = true;
        }

        if (notifyChanged)
        {
            Changed?.Invoke();
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                GovernedReviewPendingRequest? canceledRequest = null;
                bool notifyCanceled = false;
                lock (gate)
                {
                    if (pendingRequest is null
                        || !pendingRequest.Request.SessionId.Equals(request.SessionId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    canceledRequest = pendingRequest;
                    pendingRequest = null;
                    notifyCanceled = true;
                }

                canceledRequest?.Completion.TrySetCanceled(cancellationToken);

                if (notifyCanceled)
                {
                    Changed?.Invoke();
                }
            });
        }

        return activeRequest.Completion.Task;
    }

    public void Complete(string sessionId, GovernedReviewResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        GovernedReviewPendingRequest? resolvedRequest = null;
        bool notifyChanged = false;
        lock (gate)
        {
            if (pendingRequest is null)
            {
                return;
            }

            if (!pendingRequest.Request.SessionId.Equals(sessionId, StringComparison.Ordinal))
            {
                return;
            }

            resolvedRequest = pendingRequest;
            pendingRequest = null;
            notifyChanged = true;
        }

        resolvedRequest?.Completion.TrySetResult(resolution);

        if (notifyChanged)
        {
            Changed?.Invoke();
        }
    }
}

public sealed record GovernedReviewRequest(
    string SessionId,
    string RelativePath,
    int PendingCount,
    bool PreMergeValidationIsError,
    bool PreMergeValidationForceApproved);

public sealed record GovernedReviewResolution(
    string SessionId,
    bool Completed,
    bool AcceptedWithOverride,
    int RemainingPendingCount,
    string Message,
    bool NotesUpdateRequested = false,
    string? UserNotesPath = null,
    string? AgentNotesPath = null,
    string? NotesInstruction = null);

public sealed record GovernedReviewPendingRequest(
    GovernedReviewRequest Request,
    TaskCompletionSource<GovernedReviewResolution> Completion);
