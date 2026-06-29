using System.Text.Json;

namespace CodexAppServerBlazor.Services;

public sealed class PermissionRequestService
{
    private const int MaxRequests = 100;
    private readonly object gate = new();
    private readonly List<CodexPermissionRequest> requests = [];

    public IReadOnlyList<CodexPermissionRequest> GetSnapshot()
    {
        lock (gate)
        {
            return requests.ToArray();
        }
    }

    public CodexPermissionRequest Add(CodexServerRequestEvent serverRequest)
    {
        CodexPermissionRequest request = new(
            RequestId: serverRequest.RequestId,
            Method: serverRequest.Method,
            Summary: serverRequest.Summary,
            RawJson: serverRequest.RawJson,
            Status: "pending",
            CreatedAt: DateTimeOffset.Now,
            ResolvedAt: null);

        lock (gate)
        {
            requests.RemoveAll(item => item.RequestId == request.RequestId);
            requests.Add(request);
            if (requests.Count > MaxRequests)
            {
                requests.RemoveRange(0, requests.Count - MaxRequests);
            }
        }

        return request;
    }

    public CodexPermissionRequest? Resolve(int requestId, string status)
    {
        lock (gate)
        {
            int index = requests.FindIndex(item => item.RequestId == requestId);
            if (index < 0)
            {
                return null;
            }

            CodexPermissionRequest resolved = requests[index] with
            {
                Status = status,
                ResolvedAt = DateTimeOffset.Now
            };
            requests[index] = resolved;
            return resolved;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            requests.Clear();
        }
    }

    public static object CreateDenyResponse(string method, bool cancelTurn)
    {
        if (method.Equals("mcpServer/elicitation/request", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                action = cancelTurn ? "cancel" : "decline"
            };
        }

        if (method.Equals("item/permissions/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                permissions = new
                {
                    fileSystem = (object?)null,
                    network = (object?)null
                },
                scope = "turn",
                strictAutoReview = true
            };
        }

        if (method.Equals("execCommandApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("applyPatchApproval", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                decision = cancelTurn ? "abort" : "denied"
            };
        }

        return new
        {
            decision = cancelTurn ? "cancel" : "decline"
        };
    }

    public static string DescribeResponse(object response)
    {
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

public sealed record CodexPermissionRequest(
    int RequestId,
    string Method,
    string Summary,
    string RawJson,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);
