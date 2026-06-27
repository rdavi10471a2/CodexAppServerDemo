namespace CodexAppServerBlazor.Services;

public sealed class DirectoryBrowserService
{
    public DirectoryBrowserSnapshot GetSnapshot(string? requestedPath)
    {
        string resolvedPath = ResolveRequestedPath(requestedPath);
        string? errorMessage = null;
        if (!Directory.Exists(resolvedPath))
        {
            errorMessage = "Directory does not exist.";
            resolvedPath = Directory.GetCurrentDirectory();
        }

        DirectoryEntryViewModel[] drives = GetDrives();
        DirectoryEntryViewModel[] children = [];
        try
        {
            children = Directory.EnumerateDirectories(resolvedPath)
                .Select(path => new DirectoryInfo(path))
                .Where(info => !IsHiddenOrSystem(info.Attributes))
                .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(info => new DirectoryEntryViewModel(info.Name, info.FullName))
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            errorMessage = ex.Message;
        }

        string? parentPath = Directory.GetParent(resolvedPath)?.FullName;
        DirectoryEntryViewModel[] browseChoices = GetBrowseChoices(resolvedPath, parentPath, drives, children);
        return new DirectoryBrowserSnapshot(
            Path.GetFullPath(resolvedPath),
            parentPath,
            drives,
            children,
            browseChoices,
            errorMessage);
    }

    private static string ResolveRequestedPath(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Directory.GetCurrentDirectory();
        }

        string expanded = Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"'));
        if (File.Exists(expanded))
        {
            return Path.GetDirectoryName(Path.GetFullPath(expanded)) ?? Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(expanded);
    }

    private static DirectoryEntryViewModel[] GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive => new DirectoryEntryViewModel(drive.Name, drive.RootDirectory.FullName))
            .ToArray();
    }

    private static DirectoryEntryViewModel[] GetBrowseChoices(
        string currentPath,
        string? parentPath,
        IReadOnlyList<DirectoryEntryViewModel> drives,
        IReadOnlyList<DirectoryEntryViewModel> children)
    {
        List<DirectoryEntryViewModel> choices = [];
        choices.Add(new DirectoryEntryViewModel("Current folder", currentPath));
        if (!string.IsNullOrWhiteSpace(parentPath))
        {
            choices.Add(new DirectoryEntryViewModel("Parent folder", parentPath));
        }

        AddKnownFolder(choices, "User profile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddKnownFolder(choices, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddKnownFolder(choices, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        choices.AddRange(drives.Select(drive => new DirectoryEntryViewModel($"Drive {drive.Name}", drive.FullPath)));
        choices.AddRange(children.Select(child => new DirectoryEntryViewModel(child.Name, child.FullPath)));
        return choices
            .GroupBy(choice => choice.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddKnownFolder(List<DirectoryEntryViewModel> choices, string name, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            choices.Add(new DirectoryEntryViewModel(name, path));
        }
    }

    private static bool IsHiddenOrSystem(FileAttributes attributes)
    {
        return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
    }
}

public sealed record DirectoryBrowserSnapshot(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<DirectoryEntryViewModel> Drives,
    IReadOnlyList<DirectoryEntryViewModel> Children,
    IReadOnlyList<DirectoryEntryViewModel> BrowseChoices,
    string? ErrorMessage)
{
    public static DirectoryBrowserSnapshot Empty { get; } = new(
        string.Empty,
        null,
        [],
        [],
        [],
        null);
}

public sealed record DirectoryEntryViewModel(string Name, string FullPath);
