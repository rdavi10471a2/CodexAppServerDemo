using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerWinForms.Mcp;

public static class McpHostFactory
{
    public const string LocalMcpUrl = "http://localhost:6278";
    public const string HealthPath = "/health";

    public static IHost Create(WorkspaceState workspaceState)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(LocalMcpUrl);
        builder.Services.AddSingleton(workspaceState);
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
            tool = nameof(WorkspaceMcpTools.GetWorkspace)
        }));
        app.MapMcp();

        return app;
    }
}
