using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class HarnessMcpHostedService : IHostedService, IDisposable
{
    private readonly WorkspaceState workspaceState;
    private readonly SourceWorkspaceService sourceWorkspaceService;
    private readonly IConfiguration configuration;
    private IHost? host;

    public HarnessMcpHostedService(
        WorkspaceState workspaceState,
        SourceWorkspaceService sourceWorkspaceService,
        IConfiguration configuration)
    {
        this.workspaceState = workspaceState;
        this.sourceWorkspaceService = sourceWorkspaceService;
        this.configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (host is not null)
        {
            return Task.CompletedTask;
        }

        string mcpUrl = configuration["Mcp:Url"] ?? McpHostFactory.DefaultLocalMcpUrl;
        host = McpHostFactory.Create(workspaceState, sourceWorkspaceService, mcpUrl);
        return host.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (host is null)
        {
            return;
        }

        await host.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (host is not null)
        {
            host.Dispose();
        }
    }
}
