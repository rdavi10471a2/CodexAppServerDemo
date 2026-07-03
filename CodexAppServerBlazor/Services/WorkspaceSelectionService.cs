namespace CodexAppServerBlazor.Services;

public sealed class WorkspaceSelectionService
{
    private readonly IConfiguration configuration;
    private readonly string persistencePath;

    public WorkspaceSelectionService(IConfiguration configuration)
    {
        this.configuration = configuration;
        persistencePath = configuration["Workspace:PersistencePath"] is string configuredPath
            && !string.IsNullOrWhiteSpace(configuredPath)
            ? ResolvePersistencePath(configuredPath)
            : Path.Combine(
                AppContext.BaseDirectory,
                "runtime",
                "app-state",
                "selected-workspace.txt");
    }

    public string GetStartupWorkspace()
    {
        string? persistedWorkspace = TryReadPersistedWorkspace();
        if (!string.IsNullOrWhiteSpace(persistedWorkspace))
        {
            return persistedWorkspace;
        }

        return configuration["Workspace:DefaultCwd"] ?? Directory.GetCurrentDirectory();
    }

    public void SaveWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException("Workspace root is required.");
        }

        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(persistencePath) ?? AppContext.BaseDirectory);
        File.WriteAllText(persistencePath, fullWorkspaceRoot + Environment.NewLine);
    }

    private string? TryReadPersistedWorkspace()
    {
        if (!File.Exists(persistencePath))
        {
            return null;
        }

        try
        {
            string path = File.ReadAllText(persistencePath).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath)
                ? fullPath
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolvePersistencePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}
