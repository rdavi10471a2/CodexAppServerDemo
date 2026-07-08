namespace CodexAppServerBlazor.Services.Workflow;

// DORMANT as of the elicitation-gate change. The governed staged-review accept/reject gate is now driven by
// MCP elicitation inside HarnessWorkspaceReviewService (see IReviewElicitor); the MCP path no longer queues
// through this coordinator. It remains registered only so the existing Home.razor.cs dialog scaffolding keeps
// compiling. Nothing calls QueueAndWaitAsync anymore, so its pending request never becomes non-null and the
// old dialogs never fire. Safe to delete once the elicitation gate is confirmed and Home.razor.cs is cleaned.
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
    string Message);

public sealed record GovernedReviewPendingRequest(
    GovernedReviewRequest Request,
    TaskCompletionSource<GovernedReviewResolution> Completion);
