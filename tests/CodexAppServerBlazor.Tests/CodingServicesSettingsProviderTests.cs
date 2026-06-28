using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

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
        CodingServicesSettingsProvider provider = new(configuration, new TestHostEnvironment(workspaceRoot));

        CodingServicesSettings settings = provider.GetSettings(workspaceRoot);

        Assert.Equal(workspaceRoot, settings.RepositoryRoot);
        Assert.Equal(Path.GetFullPath(solutionPath), settings.WatchedSolutionPath);
        Assert.Equal(Path.Combine(workspaceRoot, "runtime"), settings.RuntimeRoot);
        Assert.Equal(Path.GetFullPath(testProjectPath), Assert.Single(settings.TestProjectPaths));
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
        CodingServicesSettingsProvider provider = new(configuration, new TestHostEnvironment(workspaceRoot));

        CodingServicesSettings settings = provider.GetSettings(productRoot);

        Assert.Equal(Path.GetFullPath(solutionPath), settings.WatchedSolutionPath);
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
    }

    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "CodexAppServerBlazor.Tests";

    public string ContentRootPath { get; set; }

    public IFileProvider ContentRootFileProvider { get; set; }
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
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
