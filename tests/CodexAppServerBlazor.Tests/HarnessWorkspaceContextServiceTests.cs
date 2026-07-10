using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class HarnessWorkspaceContextServiceTests
{
    [Fact]
    public async Task GetCurrentTaskAsync_returns_active_task_when_present()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string solutionPath = Path.Combine(repository.RootPath, "Sample.slnx");
        File.WriteAllText(solutionPath, "<Solution />");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
            })
            .Build();

        CodingServicesSettingsProvider settingsProvider = new(configuration);
        WorkflowTaskBoardRepository taskRepository = CreateRepository(settingsProvider, repository.RootPath);
        WorkflowTaskBoardSnapshot snapshot = taskRepository.LoadSnapshot();
        WorkflowTaskRow activeTask = snapshot.Tasks.Single(task => task.StateCode.Equals("Active", StringComparison.Ordinal));

        HarnessWorkspaceContextService service = CreateService(configuration, repository.RootPath);

        CurrentTaskResult result = await service.GetCurrentTaskAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.HasCurrentTask);
        Assert.Equal(activeTask.Id, result.TaskId);
        Assert.Equal(activeTask.TaskNumber, result.TaskNumber);
        Assert.Equal("TASK-0001", result.TaskLabel);
        Assert.Equal(activeTask.Name, result.TaskName);
        Assert.Equal(activeTask.StateCode, result.TaskStateCode);
    }

    [Fact]
    public async Task GetCurrentTaskAsync_reports_no_current_task_when_none_is_active()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string solutionPath = Path.Combine(repository.RootPath, "Sample.slnx");
        File.WriteAllText(solutionPath, "<Solution />");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
            })
            .Build();

        CodingServicesSettingsProvider settingsProvider = new(configuration);
        WorkflowTaskBoardRepository taskRepository = CreateRepository(settingsProvider, repository.RootPath);
        WorkflowTaskBoardSnapshot snapshot = taskRepository.LoadSnapshot();
        WorkflowTaskRow activeTask = snapshot.Tasks.Single(task => task.StateCode.Equals("Active", StringComparison.Ordinal));
        taskRepository.MoveTask(activeTask.Id, "Proposed");

        HarnessWorkspaceContextService service = CreateService(configuration, repository.RootPath);

        CurrentTaskResult result = await service.GetCurrentTaskAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.HasCurrentTask);
        Assert.Null(result.TaskId);
        Assert.Contains("No Active task", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildSolutionIndexAsync_reports_missing_workspace_when_unset()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
            })
            .Build();

        WorkspaceState workspaceState = new();
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
        HarnessWorkspaceContextService service = new(workspaceState, sourceWorkspaceService, settingsProvider);

        ReindexWorkspaceResult result = await service.RebuildSolutionIndexAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No workspace CWD", result.Error, StringComparison.Ordinal);
    }

    private static HarnessWorkspaceContextService CreateService(IConfiguration configuration, string workspaceRoot)
    {
        WorkspaceState workspaceState = new();
        workspaceState.SetRepoRoot(workspaceRoot);
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
        return new HarnessWorkspaceContextService(workspaceState, sourceWorkspaceService, settingsProvider);
    }

    private static WorkflowTaskBoardRepository CreateRepository(CodingServicesSettingsProvider settingsProvider, string workspaceRoot)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        return new WorkflowTaskBoardRepository(
            SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
            SystemDataPaths.GetDefaultTaskMemoryRoot(settings));
    }
}
