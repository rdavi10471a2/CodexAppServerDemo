using System.Text;

namespace CodexAppServerBlazor.Services;

public sealed class WorkspaceSelectionService
{
    private const int SaveWorkspaceRetryCount = 8;
    private static readonly TimeSpan SaveWorkspaceRetryDelay = TimeSpan.FromMilliseconds(50);

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
        if (string.Equals(TryReadPersistedWorkspace(), fullWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Exception? lastException = null;
        for (int attempt = 0; attempt < SaveWorkspaceRetryCount; attempt++)
        {
            try
            {
                using FileStream stream = new(
                    persistencePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);
                using StreamWriter writer = new(stream, Encoding.UTF8);
                writer.Write(fullWorkspaceRoot);
                writer.Write(Environment.NewLine);
                return;
            }
            catch (IOException ex) when (attempt < SaveWorkspaceRetryCount - 1)
            {
                lastException = ex;
                Thread.Sleep(SaveWorkspaceRetryDelay);
            }
            catch (UnauthorizedAccessException ex) when (attempt < SaveWorkspaceRetryCount - 1)
            {
                lastException = ex;
                Thread.Sleep(SaveWorkspaceRetryDelay);
            }
        }

        if (lastException is UnauthorizedAccessException unauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"Workspace selection persistence file remained locked: {persistencePath}",
                unauthorizedAccessException);
        }

        throw new IOException(
            $"Workspace selection persistence file remained locked: {persistencePath}",
            lastException);
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
