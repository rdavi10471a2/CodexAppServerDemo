namespace CodexAppServerWinForms.Mcp;

/// <summary>
/// Workflow seam for file access exposed through MCP.
/// Keep source reads here instead of letting the WinForms Codex client inline file
/// content directly into prompts. This is the harness boundary.
/// </summary>
public sealed class HarnessFileWorkflow
{
    private const long DefaultContentLimitBytes = 300_000;

    private readonly SelectedFileState _selectedFileState;

    public HarnessFileWorkflow(SelectedFileState selectedFileState)
    {
        _selectedFileState = selectedFileState;
    }

    public async Task<SelectedFileResult> GetSelectedFileAsync(
        bool includeContent,
        CancellationToken cancellationToken)
    {
        var filePath = _selectedFileState.FilePath;
        var repoRoot = _selectedFileState.RepoRoot;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return SelectedFileResult.Fail(
                repoRoot,
                filePath: null,
                fileName: null,
                relativePath: null,
                length: null,
                error: "No file has been selected in the WinForms harness.");
        }

        if (!File.Exists(filePath))
        {
            return SelectedFileResult.Fail(
                repoRoot,
                filePath,
                Path.GetFileName(filePath),
                GetRelativePathOrNull(repoRoot, filePath),
                length: null,
                error: "The selected file no longer exists.");
        }

        var info = new FileInfo(filePath);
        var relativePath = GetRelativePathOrNull(repoRoot, filePath);
        string? content = null;

        if (includeContent)
        {
            if (info.Length > DefaultContentLimitBytes)
            {
                return SelectedFileResult.Fail(
                    repoRoot,
                    filePath,
                    info.Name,
                    relativePath,
                    info.Length,
                    $"Selected file is larger than the {DefaultContentLimitBytes:N0} byte harness MCP limit.");
            }

            content = await File.ReadAllTextAsync(filePath, cancellationToken);
        }

        return new SelectedFileResult(
            Success: true,
            RepoRoot: repoRoot,
            FilePath: filePath,
            RelativePath: relativePath,
            FileName: info.Name,
            Length: info.Length,
            Content: content,
            Error: null);
    }

    private static string? GetRelativePathOrNull(string? repoRoot, string filePath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
            return null;

        try
        {
            return Path.GetRelativePath(repoRoot, filePath);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record SelectedFileResult(
    bool Success,
    string? RepoRoot,
    string? FilePath,
    string? RelativePath,
    string? FileName,
    long? Length,
    string? Content,
    string? Error)
{
    public static SelectedFileResult Fail(
        string? repoRoot,
        string? filePath,
        string? fileName,
        string? relativePath,
        long? length,
        string error)
    {
        return new SelectedFileResult(
            Success: false,
            RepoRoot: repoRoot,
            FilePath: filePath,
            RelativePath: relativePath,
            FileName: fileName,
            Length: length,
            Content: null,
            Error: error);
    }
}
