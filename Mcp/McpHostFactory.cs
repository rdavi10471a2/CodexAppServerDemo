using System.ComponentModel;
using System.Reflection;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;
using ModelContextProtocol.Server;

namespace CodexAppServerBlazor.Mcp;

public static class McpHostFactory
{
    public const string DefaultLocalMcpUrl = "http://localhost:6278";
    public const string HealthPath = "/health";
    private static readonly Type[] RegisteredToolTypes =
    [
        typeof(WorkspaceMcpTools),
        typeof(WorkspaceEditMcpTools),
        typeof(WorkspaceReviewMcpTools),
        typeof(ElicitationProbeMcpTools)
    ];

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
            .WithTools<WorkspaceReviewMcpTools>()
            .WithTools<ElicitationProbeMcpTools>();

        var app = builder.Build();
        app.MapGet(HealthPath, () => Results.Json(new
        {
            status = "ok",
            mcpEndpoint = endpointUrl,
            transport = "streamable-http",
            stateless = false,
            requiredAccept = "application/json, text/event-stream",
            responseFormat = "Successful MCP calls are returned as text/event-stream frames with JSON content in event: message payloads.",
            tools = GetHealthToolInventory()
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

    public static IReadOnlyList<McpHealthToolInventoryItem> GetHealthToolInventory()
    {
        List<McpHealthToolInventoryItem> tools = [];
        foreach (Type toolType in RegisteredToolTypes)
        {
            foreach (MethodInfo method in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                         .OrderBy(method => method.MetadataToken))
            {
                string name = ToSnakeCase(method.Name);
                string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? "No tool description available.";
                tools.Add(new McpHealthToolInventoryItem(name, description));
            }
        }

        return tools;
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        List<char> buffer = new(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            bool insertUnderscore =
                i > 0
                && char.IsUpper(current)
                && (!char.IsUpper(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1])));
            if (insertUnderscore)
            {
                buffer.Add('_');
            }

            buffer.Add(char.ToLowerInvariant(current));
        }

        return new string(buffer.ToArray());
    }
}

public sealed record McpHealthToolInventoryItem(string Name, string Description);
