using CodexAppServerWinForms.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class HarnessMcpHostedService : IHostedService, IDisposable
{
    private readonly SelectedFileState selectedFileState;
    private IHost? host;

    public HarnessMcpHostedService(SelectedFileState selectedFileState)
    {
        this.selectedFileState = selectedFileState;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (host is not null)
        {
            return Task.CompletedTask;
        }

        host = McpHostFactory.Create(selectedFileState);
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
