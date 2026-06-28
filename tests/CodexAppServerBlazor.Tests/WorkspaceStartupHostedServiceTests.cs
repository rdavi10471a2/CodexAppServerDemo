using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkspaceStartupHostedServiceTests
{
    [Fact]
    public async Task StartAsync_selects_default_cwd_as_active_workspace()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            string solutionPath = Path.Combine(repository.RootPath, "Sample.slnx");
            File.WriteAllText(solutionPath, "<Solution />");
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Workspace:DefaultCwd"] = repository.RootPath,
                    ["CodexAppServer:AutoStartOnStartup"] = "false",
                    ["CodingServices:RuntimeRoot"] = "runtime",
                    ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
                })
                .Build();
            WorkspaceState workspaceState = new();
            CodingServicesSettingsProvider settingsProvider = new(configuration);
            SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
            CodexConnectionService connectionService = new(workspaceState, sourceWorkspaceService);
            WorkspaceStartupHostedService service = new(
                configuration,
                workspaceState,
                sourceWorkspaceService,
                connectionService);

            await service.StartAsync(CancellationToken.None);

            try
            {
                string expectedRoot = Path.GetFullPath(repository.RootPath);
                string? actualRoot = await WaitForWorkspaceRootAsync(workspaceState, expectedRoot);

                Assert.Equal(expectedRoot, actualRoot);
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
                await connectionService.DisposeAsync();
            }
        }
    }

    private static async Task<string?> WaitForWorkspaceRootAsync(WorkspaceState workspaceState, string expectedRoot)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            string? currentRoot = workspaceState.RepoRoot;
            if (string.Equals(currentRoot, expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return currentRoot;
            }

            await Task.Delay(25);
        }

        return workspaceState.RepoRoot;
    }
}
