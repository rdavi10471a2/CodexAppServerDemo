using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class CodingServicesSettingsProviderTests
{
    [Fact]
    public void GetSettings_resolves_watched_solution_runtime_and_test_projects()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string workspaceRoot = repository.RootPath;
        string solutionPath = Path.Combine(workspaceRoot, "Sample.slnx");
        string testProjectPath = Path.Combine(workspaceRoot, "tests", "Sample.Tests", "Sample.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(testProjectPath)!);
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(testProjectPath, "<Project />");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "Sample.slnx",
                ["CodingServices:TestProjectPaths:0"] = "tests/Sample.Tests/Sample.Tests.csproj"
            })
            .Build();
        CodingServicesSettingsProvider provider = new(configuration);

        CodingServicesSettings settings = provider.GetSettings(workspaceRoot);

        Assert.Equal(workspaceRoot, settings.RepositoryRoot);
        Assert.Equal(Path.GetFullPath(solutionPath), settings.WatchedSolutionPath);
        Assert.Equal(Path.Combine(workspaceRoot, "runtime"), settings.RuntimeRoot);
        Assert.Equal(Path.GetFullPath(testProjectPath), Assert.Single(settings.TestProjectPaths));
    }

    [Fact]
    public void GetSettings_resolves_relative_test_projects_against_selected_workspace()
    {
        using (TemporaryRepository appRepository = TemporaryRepository.Create())
        using (TemporaryRepository selectedWorkspace = TemporaryRepository.Create())
        {
            string appRepoTestProjectPath = Path.Combine(appRepository.RootPath, "tests", "Sample.Tests", "Sample.Tests.csproj");
            string workspaceTestProjectPath = Path.Combine(selectedWorkspace.RootPath, "tests", "Sample.Tests", "Sample.Tests.csproj");
            string workspaceSolutionPath = Path.Combine(selectedWorkspace.RootPath, "Sample.slnx");
            Directory.CreateDirectory(Path.GetDirectoryName(appRepoTestProjectPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(workspaceTestProjectPath)!);
            File.WriteAllText(appRepoTestProjectPath, "<Project />");
            File.WriteAllText(workspaceTestProjectPath, "<Project />");
            File.WriteAllText(workspaceSolutionPath, "<Solution />");

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CodingServices:RuntimeRoot"] = "runtime",
                    ["CodingServices:WatchedSolutionPath"] = "Sample.slnx",
                    ["CodingServices:TestProjectPaths:0"] = "tests/Sample.Tests/Sample.Tests.csproj"
                })
                .Build();
            CodingServicesSettingsProvider provider = new(configuration);

            CodingServicesSettings settings = provider.GetSettings(selectedWorkspace.RootPath);

            Assert.Equal(Path.GetFullPath(selectedWorkspace.RootPath), settings.RepositoryRoot);
            Assert.Equal(Path.Combine(selectedWorkspace.RootPath, "runtime"), settings.RuntimeRoot);
            Assert.Equal(Path.GetFullPath(workspaceTestProjectPath), Assert.Single(settings.TestProjectPaths));
            Assert.NotEqual(Path.GetFullPath(appRepoTestProjectPath), settings.TestProjectPaths[0]);
        }
    }

    [Fact]
    public void GetSettings_uses_first_solution_when_path_is_not_configured()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string workspaceRoot = repository.RootPath;
        string productRoot = Path.Combine(workspaceRoot, "product");
        Directory.CreateDirectory(productRoot);
        string solutionPath = Path.Combine(productRoot, "Sample.slnx");
        File.WriteAllText(solutionPath, "<Solution />");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime"
            })
            .Build();
        CodingServicesSettingsProvider provider = new(configuration);

        CodingServicesSettings settings = provider.GetSettings(productRoot);

        Assert.Equal(Path.GetFullPath(solutionPath), settings.WatchedSolutionPath);
    }

    [Theory]
    [InlineData(null, "http://localhost:6278")]
    [InlineData(" http://localhost:6278/ ", "http://localhost:6278")]
    [InlineData("http://127.0.0.1:6278", "http://127.0.0.1:6278")]
    [InlineData("http://[::1]:6278", "http://[::1]:6278")]
    public void NormalizeLocalUrl_accepts_loopback_urls(string? configuredUrl, string expectedUrl)
    {
        string normalizedUrl = McpHostFactory.NormalizeLocalUrl(configuredUrl, McpHostFactory.DefaultLocalMcpUrl);

        Assert.Equal(expectedUrl, normalizedUrl);
    }

    [Theory]
    [InlineData("http://0.0.0.0:6278")]
    [InlineData("http://192.168.1.10:6278")]
    [InlineData("http://example.com:6278")]
    [InlineData("ftp://localhost:6278")]
    [InlineData("http://localhost:6278/mcp")]
    public void NormalizeLocalUrl_rejects_nonlocal_or_invalid_urls(string configuredUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            McpHostFactory.NormalizeLocalUrl(configuredUrl, McpHostFactory.DefaultLocalMcpUrl));
    }
}

internal sealed class TemporaryRepository : IDisposable
{
    private TemporaryRepository(string rootPath)
    {
        RootPath = rootPath;
        File.WriteAllText(Path.Combine(rootPath, "CodexAppServerWinForms_corrected.slnx"), "<Solution />");
    }

    public string RootPath { get; }

    public static TemporaryRepository Create()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"coding-services-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        return new TemporaryRepository(rootPath);
    }

    public void Dispose()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }
}
