namespace CodexAppServerBlazor.Mcp;

public sealed class WorkspaceState
{
    private readonly object gate = new();
    private string? repoRoot;
    private string? currentEditSessionId;

    public string? RepoRoot
    {
        get
        {
            lock (gate)
            {
                return repoRoot;
            }
        }
    }

    public string? CurrentEditSessionId
    {
        get
        {
            lock (gate)
            {
                return currentEditSessionId;
            }
        }
    }

    public void SetRepoRoot(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repoRoot));
        }

        string fullPath = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Repository root does not exist: {fullPath}");
        }

        lock (gate)
        {
            this.repoRoot = fullPath;
        }
    }

    public void SetCurrentEditSessionId(string? sessionId)
    {
        lock (gate)
        {
            currentEditSessionId = string.IsNullOrWhiteSpace(sessionId)
                ? null
                : sessionId.Trim();
        }
    }
}
