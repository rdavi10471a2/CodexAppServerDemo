using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerWinForms.Mcp;

public static class McpHostFactory
{
    public const string LocalMcpUrl = "http://localhost:6278";

    public static IHost Create(SelectedFileState selectedFileState)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls(LocalMcpUrl);
        builder.Services.AddSingleton(selectedFileState);
        builder.Services.AddSingleton<HarnessFileWorkflow>();

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<FileMcpTools>();

        var app = builder.Build();
        app.MapMcp();

        return app;
    }
}
