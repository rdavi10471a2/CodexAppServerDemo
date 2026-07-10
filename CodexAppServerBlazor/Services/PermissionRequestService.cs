using System.Text.Json;
using System.Text.Json.Nodes;

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
            CorrelationId: NormalizeOptionalValue(serverRequest.CorrelationId),
            ApprovalId: NormalizeOptionalValue(serverRequest.ApprovalId),
            SupportsSessionApproval: SupportsSessionApproval(serverRequest.Method),
            SupportsPersistentApproval: SupportsPersistentApproval(serverRequest.Method, serverRequest.RawJson),
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

    public CodexPermissionRequest? ResolveCommandResult(string? correlationId, string? approvalId, string status)
    {
        lock (gate)
        {
            int index = !string.IsNullOrWhiteSpace(approvalId)
                ? requests.FindLastIndex(request => IsApprovedAndAwaitingTrackedResult(request)
                    && string.Equals(request.ApprovalId, approvalId, StringComparison.Ordinal))
                : -1;
            if (index < 0 && !string.IsNullOrWhiteSpace(correlationId))
            {
                index = requests.FindLastIndex(request => IsApprovedAndAwaitingTrackedResult(request)
                    && string.Equals(request.CorrelationId, correlationId, StringComparison.Ordinal));
            }
            if (index < 0)
            {
                index = requests.FindLastIndex(IsApprovedAndAwaitingTrackedResult);
            }

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

    private static bool IsApprovedAndWaiting(CodexPermissionRequest request)
    {
        return request.Status.Contains("approved", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApprovedAndAwaitingTrackedResult(CodexPermissionRequest request)
    {
        return IsApprovedAndWaiting(request)
            && SupportsTrackedResult(request.Method);
    }

    private static bool SupportsTrackedResult(string method)
    {
        return method.Equals("item/commandExecution/requestApproval", StringComparison.OrdinalIgnoreCase)
            || method.Equals("execCommandApproval", StringComparison.OrdinalIgnoreCase)
            || method.Equals("item/fileChange/requestApproval", StringComparison.OrdinalIgnoreCase)
            || method.Equals("applyPatchApproval", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    public static bool IsElicitationRequest(string method)
    {
        return method.Equals("mcpServer/elicitation/request", StringComparison.OrdinalIgnoreCase);
    }

    public static object CreateDenyResponse(string method, bool cancelTurn)
    {
        if (IsElicitationRequest(method))
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

    public static object CreateApproveResponse(string method, string rawJson, PermissionApprovalScope scope)
    {
        if (IsElicitationRequest(method))
        {
            return new
            {
                action = "accept",
                content = new { }
            };
        }

        if (method.Equals("item/permissions/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            JsonNode? requestedPermissions = TryReadRequestedPermissions(rawJson);
            return new
            {
                permissions = requestedPermissions ?? new JsonObject(),
                scope = scope == PermissionApprovalScope.Session ? "session" : "turn",
                strictAutoReview = true
            };
        }

        if (method.Equals("item/commandExecution/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            if (scope == PermissionApprovalScope.Persistent)
            {
                JsonArray? amendment = TryReadProposedExecpolicyAmendment(rawJson);
                if (amendment is not null)
                {
                    return new
                    {
                        decision = new
                        {
                            acceptWithExecpolicyAmendment = new
                            {
                                execpolicy_amendment = amendment
                            }
                        }
                    };
                }
            }

            return new
            {
                decision = scope == PermissionApprovalScope.Session ? "acceptForSession" : "accept"
            };
        }

        if (method.Equals("item/fileChange/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                decision = scope == PermissionApprovalScope.Session ? "acceptForSession" : "accept"
            };
        }

        if (method.Equals("execCommandApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("applyPatchApproval", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                decision = scope == PermissionApprovalScope.Session ? "approved_for_session" : "approved"
            };
        }

        return new
        {
            decision = "accept"
        };
    }

    private static bool SupportsSessionApproval(string method)
    {
        return method.Equals("item/commandExecution/requestApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("item/permissions/requestApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("item/fileChange/requestApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("execCommandApproval", StringComparison.OrdinalIgnoreCase) ||
            method.Equals("applyPatchApproval", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsPersistentApproval(string method, string rawJson)
    {
        if (!method.Equals("item/commandExecution/requestApproval", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        JsonArray? amendment = TryReadProposedExecpolicyAmendment(rawJson);
        return amendment is not null && amendment.Count > 0;
    }

    private static JsonNode? TryReadRequestedPermissions(string rawJson)
    {
        try
        {
            JsonNode? request = JsonNode.Parse(rawJson);
            JsonNode? permissions = request?["params"]?["permissions"];
            return permissions?.DeepClone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonArray? TryReadProposedExecpolicyAmendment(string rawJson)
    {
        try
        {
            JsonNode? request = JsonNode.Parse(rawJson);
            JsonArray? amendment = request?["params"]?["proposedExecpolicyAmendment"]?.AsArray();
            return amendment?.DeepClone().AsArray();
        }
        catch (JsonException)
        {
            return null;
        }
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
    string? CorrelationId,
    string? ApprovalId,
    bool SupportsSessionApproval,
    bool SupportsPersistentApproval,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public enum PermissionApprovalScope
{
    Turn,
    Session,
    Persistent
}
