using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.AICodingServices.Data;

public sealed record WatchedSolutionStructureResult(
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    DateTimeOffset IndexedAtUtc,
    int ProjectCount,
    int DocumentCount,
    int SkippedDocumentCount,
    int ReturnedDocumentCount,
    int PageSize,
    bool HasMore,
    int? NextSkipFiles,
    IReadOnlyList<WatchedProjectStructure> Projects);

public sealed record WatchedProjectStructure(
    string Name,
    string ProjectPath,
    string RelativeProjectPath,
    string Language,
    string TargetFramework,
    string TargetFrameworks,
    int DocumentCount,
    int SkippedDocumentCount,
    int ReturnedDocumentCount,
    bool HasMore,
    IReadOnlyList<WatchedSourceTreeNode> Children);

public sealed record WatchedSourceTreeNode(
    string Name,
    string Kind,
    string RelativePath,
    string ProjectRelativePath,
    string Extension,
    int DocumentCount,
    IReadOnlyList<WatchedSourceTreeNode> Children);

public static class WatchedSolutionStructureBuilder
{
    public static WatchedSolutionStructureResult Build(
        CodingServicesSettings settings,
        SolutionIndexSummary summary,
        IReadOnlyList<IndexedProjectRow> projects,
        IReadOnlyList<IndexedDocumentRow> documents,
        int skipFiles = 0,
        int maxFiles = 500)
    {
        int clampedSkipFiles = Math.Max(skipFiles, 0);
        int clampedMaxFiles = Math.Clamp(maxFiles, 1, 1000);
        int remainingSkipFiles = clampedSkipFiles;
        int remainingTakeFiles = clampedMaxFiles;
        List<WatchedProjectStructure> projectStructures = [];
        foreach (IndexedProjectRow project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            IndexedDocumentRow[] projectDocuments = documents
                .Where(document => PathEquals(document.ProjectPath, project.ProjectPath))
                .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int projectSkip = Math.Min(remainingSkipFiles, projectDocuments.Length);
            remainingSkipFiles -= projectSkip;
            IndexedDocumentRow[] afterSkip = projectDocuments
                .Skip(projectSkip)
                .ToArray();
            IndexedDocumentRow[] returnedDocuments = projectDocuments
                .Skip(projectSkip)
                .Take(Math.Max(remainingTakeFiles, 0))
                .ToArray();
            remainingTakeFiles -= returnedDocuments.Length;
            projectStructures.Add(BuildProject(settings, project, projectDocuments.Length, projectSkip, afterSkip.Length, returnedDocuments));
        }

        int returnedDocumentCount = projectStructures.Sum(project => project.ReturnedDocumentCount);
        int nextSkipFiles = clampedSkipFiles + returnedDocumentCount;
        bool hasMore = nextSkipFiles < documents.Count;
        return new WatchedSolutionStructureResult(
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            summary.IndexedAtUtc,
            projects.Count,
            documents.Count,
            Math.Min(clampedSkipFiles, documents.Count),
            returnedDocumentCount,
            clampedMaxFiles,
            hasMore,
            hasMore ? nextSkipFiles : null,
            projectStructures);
    }

    private static WatchedProjectStructure BuildProject(
        CodingServicesSettings settings,
        IndexedProjectRow project,
        int documentCount,
        int skippedDocumentCount,
        int availableAfterSkip,
        IReadOnlyList<IndexedDocumentRow> returnedDocuments)
    {
        MutableNode root = new(string.Empty, "folder", string.Empty, string.Empty, string.Empty);
        string projectDirectory = Path.GetDirectoryName(project.ProjectPath) ?? settings.WatchedProjectFolder;
        foreach (IndexedDocumentRow document in returnedDocuments)
        {
            AddDocument(root, settings.WatchedProjectFolder, projectDirectory, document);
        }

        return new WatchedProjectStructure(
            project.Name,
            project.ProjectPath,
            GetRelativePath(settings.WatchedProjectFolder, project.ProjectPath),
            project.Language,
            project.TargetFramework,
            project.TargetFrameworks,
            documentCount,
            skippedDocumentCount,
            returnedDocuments.Count,
            returnedDocuments.Count < availableAfterSkip,
            root.Children
                .OrderBy(node => node.Kind.Equals("folder", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ConvertNode)
                .ToArray());
    }

    private static void AddDocument(
        MutableNode root,
        string watchedRoot,
        string projectDirectory,
        IndexedDocumentRow document)
    {
        string relativeToProject = GetRelativePath(projectDirectory, document.FilePath);
        string relativeToSolution = GetRelativePath(watchedRoot, document.FilePath);
        string[] segments = NormalizePath(relativeToProject).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(IsHiddenBuildFolder))
        {
            return;
        }

        MutableNode current = root;
        for (int index = 0; index < segments.Length; index++)
        {
            bool isFile = index == segments.Length - 1;
            string segment = segments[index];
            string projectRelativePath = string.Join('/', segments.Take(index + 1));
            string solutionRelativePath = isFile
                ? NormalizePath(relativeToSolution)
                : string.Empty;
            string extension = isFile
                ? Path.GetExtension(segment).TrimStart('.').ToUpperInvariant()
                : string.Empty;
            current = current.GetOrAdd(
                segment,
                isFile ? "file" : "folder",
                solutionRelativePath,
                projectRelativePath,
                extension);
            current.DocumentCount += isFile ? 1 : 0;
        }
    }

    private static WatchedSourceTreeNode ConvertNode(MutableNode node)
    {
        WatchedSourceTreeNode[] children = node.Children
            .OrderBy(child => child.Kind.Equals("folder", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ConvertNode)
            .ToArray();
        int documentCount = node.Kind.Equals("file", StringComparison.Ordinal)
            ? 1
            : children.Sum(child => child.DocumentCount);

        return new WatchedSourceTreeNode(
            node.Name,
            node.Kind,
            node.RelativePath,
            node.ProjectRelativePath,
            node.Extension,
            documentCount,
            children);
    }

    private static string GetRelativePath(string root, string path)
    {
        return NormalizePath(Path.GetRelativePath(root, path));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHiddenBuildFolder(string segment)
    {
        return segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("CSSourceBackups", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MutableNode
    {
        private readonly Dictionary<string, MutableNode> children = new(StringComparer.OrdinalIgnoreCase);

        public MutableNode(
            string name,
            string kind,
            string relativePath,
            string projectRelativePath,
            string extension)
        {
            Name = name;
            Kind = kind;
            RelativePath = relativePath;
            ProjectRelativePath = projectRelativePath;
            Extension = extension;
        }

        public string Name { get; }

        public string Kind { get; }

        public string RelativePath { get; }

        public string ProjectRelativePath { get; }

        public string Extension { get; }

        public int DocumentCount { get; set; }

        public IEnumerable<MutableNode> Children => children.Values;

        public MutableNode GetOrAdd(
            string name,
            string kind,
            string relativePath,
            string projectRelativePath,
            string extension)
        {
            string key = kind + ":" + name;
            if (children.TryGetValue(key, out MutableNode? child))
            {
                return child;
            }

            child = new MutableNode(name, kind, relativePath, projectRelativePath, extension);
            children.Add(key, child);
            return child;
        }
    }
}
