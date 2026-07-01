using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.Services;

public sealed class CodingServicesSettingsProvider
{
    private readonly IConfiguration configuration;

    public CodingServicesSettingsProvider(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public CodingServicesSettings GetSettings(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException("Workspace root is required.");
        }

        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string repositoryRoot = fullWorkspaceRoot;
        string runtimeRoot = ResolveConfiguredPath("CodingServices:RuntimeRoot", "runtime", repositoryRoot);
        string watchedSolutionPath = ResolveWatchedSolutionPath(fullWorkspaceRoot);
        string[]? winMergeCandidatePaths = configuration
            .GetSection("CodingServices:WinMergeCandidatePaths")
            .Get<string[]>();
        string[] testProjectPaths = ResolveConfiguredPaths(
            "CodingServices:TestProjectPaths",
            fullWorkspaceRoot);

        return CodingServicesSettings.Create(
            repositoryRoot,
            watchedSolutionPath,
            runtimeRoot,
            testProjectPaths,
            winMergeCandidatePaths);
    }

    private string ResolveWatchedSolutionPath(string workspaceRoot)
    {
        string? configuredPath = configuration["CodingServices:WatchedSolutionPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolvePath(configuredPath, workspaceRoot);
        }

        string[] solutionPaths = Directory
            .EnumerateFiles(workspaceRoot, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(workspaceRoot, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(path => Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (solutionPaths.Length == 0)
        {
            throw new InvalidOperationException($"No .slnx or .sln file was found in {workspaceRoot}.");
        }

        return solutionPaths[0];
    }

    private string ResolveConfiguredPath(string key, string defaultValue, string basePath)
    {
        string configuredValue = configuration[key] ?? defaultValue;
        return ResolvePath(configuredValue, basePath);
    }

    private string[] ResolveConfiguredPaths(string key, string basePath)
    {
        string[]? configuredValues = configuration.GetSection(key).Get<string[]>();
        if (configuredValues is null || configuredValues.Length == 0)
        {
            return [];
        }

        return configuredValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePath(value, basePath))
            .ToArray();
    }

    private static string ResolvePath(string path, string basePath)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(basePath, path));
    }
}
