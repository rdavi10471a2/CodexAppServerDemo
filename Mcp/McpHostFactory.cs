using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerWinForms.Mcp;

public static class McpHostFactory
{
    public const string LocalMcpUrl = "http://localhost:6278";
    public const string HealthPath = "/health";

    public static IHost Create(SelectedFileState selectedFileState)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(LocalMcpUrl);
        builder.Services.AddSingleton(selectedFileState);
        builder.Services.AddSingleton<HarnessFileWorkflow>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // This harness only exposes a single local file tool and does not rely on
                // long-lived MCP sessions or server-to-client callbacks.
                options.Stateless = true;
            })
            .WithTools<FileMcpTools>();

        var app = builder.Build();
        app.MapGet(HealthPath, () => Results.Json(new
        {
            status = "ok",
            mcpEndpoint = LocalMcpUrl,
            transport = "streamable-http",
            stateless = true,
            tool = nameof(FileMcpTools.GetSelectedFile)
        }));
        app.MapMcp();

        return app;
    }
}
