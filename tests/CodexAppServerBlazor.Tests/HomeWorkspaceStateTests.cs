using CodexAppServerBlazor.Components.Pages.Home;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerBlazor.Tests;

public sealed class HomeWorkspaceStateTests
{
    [Fact]
    public void OnInitialized_syncs_startup_workspace_into_shared_workspace_state()
    {
        using TemporaryRepository workspace = TemporaryRepository.Create();
        string persistencePath = Path.Combine(workspace.RootPath, "workspace-state.txt");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:DefaultCwd"] = workspace.RootPath,
                ["Workspace:PersistencePath"] = persistencePath,
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "CodexAppServerWinForms_corrected.slnx",
                ["CodexAppServer:AutoStartOnStartup"] = "false"
            })
            .Build();

        WorkspaceState workspaceState = new();
        WorkspaceSelectionService workspaceSelectionService = new(configuration);
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
        WorkspaceWorkflowContextService workspaceWorkflowContextService = new(sourceWorkspaceService);
        TaskWorkflowContextService taskWorkflowContextService = new(settingsProvider);
        SessionBootstrapPolicyService sessionBootstrapPolicyService = new(
            configuration,
            new HostingEnvironmentStub(workspace.RootPath));
        WorkflowTurnContextComposer workflowTurnContextComposer = new();
        CodexConnectionService connectionService = new(
            configuration,
            workspaceState,
            workspaceWorkflowContextService,
            taskWorkflowContextService,
            sessionBootstrapPolicyService,
            workflowTurnContextComposer);

        Home component = new()
        {
            ConnectionService = connectionService,
            DirectoryBrowser = new DirectoryBrowserService(),
            SourceWorkspace = sourceWorkspaceService,
            Configuration = configuration,
            GovernedReviewCoordinator = new GovernedReviewCoordinatorService(),
            WorkspaceSelectionService = workspaceSelectionService,
            WorkspaceState = workspaceState
        };

        InvokeLifecycle(component, "OnInitialized");

        Assert.Equal(Path.GetFullPath(workspace.RootPath), workspaceState.RepoRoot);
    }

    private static void InvokeLifecycle(object component, string methodName)
    {
        System.Reflection.MethodInfo method = component.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find lifecycle method '{methodName}'.");
        method.Invoke(component, null);
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
