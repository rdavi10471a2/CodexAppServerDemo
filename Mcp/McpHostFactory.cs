using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerBlazor.Mcp;

public static class McpHostFactory
{
    public const string LocalMcpUrl = "http://localhost:6278";
    public const string HealthPath = "/health";

    public static IHost Create(WorkspaceState workspaceState, SourceWorkspaceService sourceWorkspaceService)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(LocalMcpUrl);
        builder.Services.AddSingleton(workspaceState);
        builder.Services.AddSingleton(sourceWorkspaceService);
        builder.Services.AddSingleton<HarnessWorkspaceWorkflow>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // This harness only exposes local workspace metadata and does not rely on
                // long-lived MCP sessions or server-to-client callbacks.
                options.Stateless = true;
            })
            .WithTools<WorkspaceMcpTools>();

        var app = builder.Build();
        app.MapGet(HealthPath, () => Results.Json(new
        {
            status = "ok",
            mcpEndpoint = LocalMcpUrl,
            transport = "streamable-http",
            stateless = true,
            tools = new[]
            {
                new
                {
                    name = nameof(WorkspaceMcpTools.GetWorkspace),
                    description = "Returns the current workspace CWD selected in the Coding Services Blazor control surface."
                },
                new
                {
                    name = nameof(WorkspaceMcpTools.GetWatchedSolutionDigest),
                    description = "Cheap readiness and change-detection metadata for the watched solution: counts, summary size, hash, and index paths."
                },
                new
                {
                    name = nameof(WorkspaceMcpTools.GetWatchedSolutionSummary),
                    description = "Full indexed project/file/type/member tree for on-demand discovery. Does not include source file bodies."
                }
            }
        }));
        app.MapMcp();

        return app;
    }
}
