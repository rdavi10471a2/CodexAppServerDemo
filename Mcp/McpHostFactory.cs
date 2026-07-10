using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Mcp;

public static class McpHostFactory
{
    public const string DefaultLocalMcpUrl = "http://localhost:6278";
    public const string HealthPath = "/health";

    public static IHost Create(
        WorkspaceState workspaceState,
        SourceWorkspaceService sourceWorkspaceService,
        string? localMcpUrl)
    {
        string endpointUrl = NormalizeLocalUrl(localMcpUrl, DefaultLocalMcpUrl);
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(endpointUrl);
        builder.Services.AddSingleton(workspaceState);
        builder.Services.AddSingleton(sourceWorkspaceService);
        builder.Services.AddSingleton<HarnessWorkspaceContextService>();
        builder.Services.AddSingleton<CodingServicesSettingsProvider>();
        builder.Services.AddSingleton<HarnessWorkspaceEditService>();
        builder.Services.AddSingleton<HarnessWorkspaceReviewService>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Governed review waits depend on a stateful MCP conversation between
                // the Blazor host and the active tool call, so transport must preserve session state.
                options.Stateless = false;
            })
            .WithTools<WorkspaceMcpTools>()
            .WithTools<WorkspaceEditMcpTools>()
            .WithTools<WorkspaceReviewMcpTools>();

        var app = builder.Build();
        app.MapGet(HealthPath, () => Results.Json(new
        {
            status = "ok",
            mcpEndpoint = endpointUrl,
            transport = "streamable-http",
            stateless = false,
            requiredAccept = "application/json, text/event-stream",
            responseFormat = "Successful MCP calls are returned as text/event-stream frames with JSON content in event: message payloads.",
            tools = new[]
            {
                new
                {
                    name = "get_workspace",
                    description = "Returns the current workspace CWD selected in the Coding Services Blazor control surface."
                },
                new
                {
                    name = "get_watched_solution_digest",
                    description = "Cheap readiness and change-detection metadata for the watched solution: counts, summary size, hash, and index paths."
                },
                new
                {
                    name = "get_watched_solution_summary",
                    description = "Product-source indexed project/file/type/member tree for on-demand discovery. Does not include source file bodies or configured test projects."
                },
                new
                {
                    name = "get_test_project_summary",
                    description = "Indexed project/file/type/member tree for configured test projects only. Does not include source file bodies."
                },
                new
                {
                    name = "get_current_task",
                    description = "Returns the current Active task for the selected workspace, or reports that no current task is set."
                },
                new
                {
                    name = "rebuild_solution_index",
                    description = "Rebuilds the watched solution index for the selected workspace and returns the refreshed readiness metadata."
                },
                new
                {
                    name = "get_edit_session_state",
                    description = "Returns the current governed edit-session status for a workspace file path."
                },
                new
                {
                    name = "refresh_file",
                    description = "Creates or refreshes the governed Working candidate for an existing workspace file. Pass sessionId to keep multi-file work inside one governed edit session."
                },
                new
                {
                    name = "new_file",
                    description = "Creates a new-file governed edit session for a file path that does not yet exist inside the selected workspace. Pass sessionId to keep multi-file work inside one governed edit session."
                },
                new
                {
                    name = "declare_session_files",
                    description = "Declares the complete governed file set for an edit session before multi-file staging."
                },
                new
                {
                    name = "add_file_to_session",
                    description = "Adds one watched workspace file to an existing governed edit session and returns the updated declared file set."
                },
                new
                {
                    name = "replace_text_in_file",
                    description = "Replaces text inside the governed Working candidate for a workspace file."
                },
                new
                {
                    name = "replace_span_in_file",
                    description = "Replaces a line/column span inside the governed Working candidate for a workspace file."
                },
                new
                {
                    name = "get_file_outline",
                    description = "Returns a Roslyn outline for a C# source file."
                },
                new
                {
                    name = "get_symbol",
                    description = "Reads a single symbol body from a C# source file in the governed Working candidate."
                },
                new
                {
                    name = "submit_symbol",
                    description = "Replaces a single symbol in a C# source file in the governed Working candidate."
                },
                new
                {
                    name = "add_field",
                    description = "Adds a field to a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "add_property",
                    description = "Adds a property to a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "add_method",
                    description = "Adds a method to a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "add_constructor",
                    description = "Adds a constructor to a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "add_nested_type",
                    description = "Adds a nested type to a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "set_type_partial",
                    description = "Adds or removes the partial modifier on a containing C# type in the governed Working candidate."
                },
                new
                {
                    name = "add_using",
                    description = "Adds a using directive to a C# source file in the governed Working candidate."
                },
                new
                {
                    name = "remove_using",
                    description = "Removes a using directive from a C# source file in the governed Working candidate."
                },
                new
                {
                    name = "remove_symbol",
                    description = "Removes a single symbol from a C# source file in the governed Working candidate."
                },
                new
                {
                    name = "list_pending_staged_reviews",
                    description = "Lists pending staged review records for the currently selected workspace."
                },
                new
                {
                    name = "load_staged_review",
                    description = "Loads the staged review model for a specific staged record id."
                },
                new
                {
                    name = "load_next_session_review",
                    description = "Loads the next pending staged review model for a staged-edit session id."
                },
                new
                {
                    name = "stage_current_candidate_for_review",
                    description = "Stages the current governed Working candidate for a single workspace file into review using an explicit sessionId and returns the review URL. Do not use this for multi-file sessions."
                },
                new
                {
                    name = "stage_edit_session_for_review",
                    description = "Stages every declared file in a governed edit session, then raises one review for the whole session."
                },
                new
                {
                    name = "accept_staged_review",
                    description = "Accepts a staged review record into watched source and records the workflow decision."
                },
                new
                {
                    name = "reject_staged_review",
                    description = "Rejects a staged review record and records the workflow decision without changing watched source."
                }
            }
        }));
        app.MapMcp();

        return app;
    }

    public static string NormalizeLocalUrl(string? configuredUrl, string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            configuredUrl = fallbackUrl;
        }

        string endpointUrl = configuredUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException($"MCP URL must be an absolute local HTTP URL: {endpointUrl}");
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MCP URL must use http or https: {endpointUrl}");
        }

        if (!uri.IsLoopback)
        {
            throw new InvalidOperationException($"MCP URL must bind to loopback only by default: {endpointUrl}");
        }

        if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException($"MCP URL must not include a path: {endpointUrl}");
        }

        return endpointUrl;
    }
}
