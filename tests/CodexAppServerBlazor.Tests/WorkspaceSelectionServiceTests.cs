using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkspaceSelectionServiceTests
{
    [Fact]
    public async Task SaveWorkspace_retries_when_persistence_file_is_temporarily_locked()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string persistencePath = Path.Combine(repository.RootPath, "selected-workspace.txt");
        string initialWorkspace = Path.Combine(repository.RootPath, "initial");
        string updatedWorkspace = Path.Combine(repository.RootPath, "updated");
        Directory.CreateDirectory(initialWorkspace);
        Directory.CreateDirectory(updatedWorkspace);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:PersistencePath"] = persistencePath
            })
            .Build();

        WorkspaceSelectionService service = new(configuration);
        service.SaveWorkspace(initialWorkspace);

        FileStream lockedStream = new(persistencePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task releaseLock = Task.Run(async () =>
        {
            await Task.Delay(150);
            lockedStream.Dispose();
        });

        service.SaveWorkspace(updatedWorkspace);
        await releaseLock;

        string persistedWorkspace = File.ReadAllText(persistencePath).Trim();
        Assert.Equal(Path.GetFullPath(updatedWorkspace), persistedWorkspace);
    }

    [Fact]
    public void SaveWorkspace_skips_rewrite_when_workspace_is_already_persisted()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string persistencePath = Path.Combine(repository.RootPath, "selected-workspace.txt");
        string workspace = Path.Combine(repository.RootPath, "current");
        Directory.CreateDirectory(workspace);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workspace:PersistencePath"] = persistencePath
            })
            .Build();

        WorkspaceSelectionService service = new(configuration);
        service.SaveWorkspace(workspace);
        DateTime firstWrite = File.GetLastWriteTimeUtc(persistencePath);

        Thread.Sleep(75);
        service.SaveWorkspace(workspace);
        DateTime secondWrite = File.GetLastWriteTimeUtc(persistencePath);

        Assert.Equal(firstWrite, secondWrite);
    }
}
