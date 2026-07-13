using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkspaceStartupHostedServiceTests
{
    [Fact]
    public async Task StartAsync_selects_default_cwd_as_active_workspace()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            string solutionPath = Path.Combine(repository.RootPath, "Sample.slnx");
            string persistencePath = Path.Combine(repository.RootPath, "workspace-state.txt");
            File.WriteAllText(solutionPath, "<Solution />");
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Workspace:DefaultCwd"] = repository.RootPath,
                    ["Workspace:PersistencePath"] = persistencePath,
                    ["CodexAppServer:AutoStartOnStartup"] = "false",
                    ["CodingServices:RuntimeRoot"] = "runtime",
                    ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
                })
                .Build();
            WorkspaceState workspaceState = new();
            WorkspaceSelectionService workspaceSelectionService = new(configuration);
            CodingServicesSettingsProvider settingsProvider = TestServiceFactory.CreateSettingsProvider(configuration, repository.RootPath);
            SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
            WorkspaceWorkflowContextService workspaceWorkflowContextService = new(sourceWorkspaceService);
            TaskWorkflowContextService taskWorkflowContextService = new(settingsProvider);
            SessionBootstrapPolicyService sessionBootstrapPolicyService = new(
                configuration,
                new HostingEnvironmentStub(repository.RootPath));
            WorkflowTurnContextComposer workflowTurnContextComposer = new();
            CodexConnectionService connectionService = new(
                configuration,
                workspaceState,
                workspaceWorkflowContextService,
                taskWorkflowContextService,
                sessionBootstrapPolicyService,
                workflowTurnContextComposer);
            WorkspaceStartupHostedService service = new(
                configuration,
                workspaceState,
                workspaceSelectionService,
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

    [Fact]
    public async Task StartAsync_prefers_persisted_workspace_and_provisions_runtime_artifacts()
    {
        using TemporaryRepository defaultWorkspace = TemporaryRepository.Create();
        using TemporaryRepository persistedWorkspace = TemporaryRepository.Create();
        string persistedSolutionPath = Path.Combine(persistedWorkspace.RootPath, "Persisted.sln");
        string persistencePath = Path.Combine(defaultWorkspace.RootPath, "workspace-state.txt");
        File.WriteAllText(persistedSolutionPath, "<Solution />");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:DefaultCwd"] = defaultWorkspace.RootPath,
                ["Workspace:PersistencePath"] = persistencePath,
                ["CodexAppServer:AutoStartOnStartup"] = "false",
                ["CodingServices:RuntimeRoot"] = "runtime"
            })
            .Build();
        WorkspaceState workspaceState = new();
        WorkspaceSelectionService workspaceSelectionService = new(configuration);
        workspaceSelectionService.SaveWorkspace(persistedWorkspace.RootPath);
        CodingServicesSettingsProvider settingsProvider = TestServiceFactory.CreateSettingsProvider(configuration, defaultWorkspace.RootPath);
        SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
        WorkspaceWorkflowContextService workspaceWorkflowContextService = new(sourceWorkspaceService);
        TaskWorkflowContextService taskWorkflowContextService = new(settingsProvider);
        SessionBootstrapPolicyService sessionBootstrapPolicyService = new(
            configuration,
            new HostingEnvironmentStub(defaultWorkspace.RootPath));
        WorkflowTurnContextComposer workflowTurnContextComposer = new();
        CodexConnectionService connectionService = new(
            configuration,
            workspaceState,
            workspaceWorkflowContextService,
            taskWorkflowContextService,
            sessionBootstrapPolicyService,
            workflowTurnContextComposer);
        WorkspaceStartupHostedService service = new(
            configuration,
            workspaceState,
            workspaceSelectionService,
            sourceWorkspaceService,
            connectionService);

        await service.StartAsync(CancellationToken.None);

        try
        {
            string expectedRoot = Path.GetFullPath(persistedWorkspace.RootPath);
            string? actualRoot = await WaitForWorkspaceRootAsync(workspaceState, expectedRoot);
            Assert.Equal(expectedRoot, actualRoot);

            CodingServicesSettings settings = settingsProvider.GetSettings(persistedWorkspace.RootPath);
            Assert.True(File.Exists(SystemDataPaths.GetDefaultPlanningDatabasePath(settings)));
            Assert.True(Directory.Exists(SystemDataPaths.GetDefaultTaskMemoryRoot(settings)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await connectionService.DisposeAsync();
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

    private sealed class HostingEnvironmentStub : IHostEnvironment
    {
        public HostingEnvironmentStub(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "CodexAppServerBlazor.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = default!;
    }
}
