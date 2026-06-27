namespace CodexAppServerWinForms.Mcp;

public sealed class SelectedFileState
{
    private readonly object _gate = new();
    private string? _repoRoot;
    private string? _filePath;

    public string? RepoRoot
    {
        get
        {
            lock (_gate)
            {
                return _repoRoot;
            }
        }
    }

    public string? FilePath
    {
        get
        {
            lock (_gate)
            {
                return _filePath;
            }
        }
    }

    public void SetRepoRoot(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
            throw new ArgumentException("Repository root is required.", nameof(repoRoot));

        var fullPath = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Repository root does not exist: {fullPath}");

        lock (_gate)
        {
            _repoRoot = fullPath;
        }
    }

    public void SetSelectedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Selected file does not exist.", fullPath);

        lock (_gate)
        {
            _filePath = fullPath;
        }
    }

    public void SetSelection(string repoRoot, string filePath)
    {
        SetRepoRoot(repoRoot);
        SetSelectedFile(filePath);
    }
}
