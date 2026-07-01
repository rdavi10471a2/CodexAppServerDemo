using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.AICodingServices.Indexing;
using Microsoft.Data.Sqlite;

namespace CodexAppServerBlazor.Services;

public sealed class SourceWorkspaceService
{
    private const long MaxReadableFileBytes = 768 * 1024;

    private readonly CodingServicesSettingsProvider settingsProvider;

    public SourceWorkspaceService(CodingServicesSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public SourceWorkspaceSnapshot BuildSnapshot(
        string workspaceRoot,
        string? selectedRelativePath,
        int? selectedLine,
        string? filter)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        return BuildSnapshot(settings, selectedRelativePath, selectedLine, filter, ProjectProjection.ProductOnly);
    }

    public SourceWorkspaceSnapshot BuildTestSnapshot(
        string workspaceRoot,
        string? selectedRelativePath,
        int? selectedLine,
        string? filter)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        if (settings.TestProjectPaths.Count == 0)
        {
            string databasePath = SystemDataPaths.GetDefaultIndexDatabasePath(settings);
            return new SourceWorkspaceSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                [],
                [],
                null,
                filter ?? string.Empty,
                "No test projects are configured in CodingServices:TestProjectPaths.");
        }

        return BuildSnapshot(settings, selectedRelativePath, selectedLine, filter, ProjectProjection.TestsOnly);
    }

    private static SourceWorkspaceSnapshot BuildSnapshot(
        CodingServicesSettings settings,
        string? selectedRelativePath,
        int? selectedLine,
        string? filter,
        ProjectProjection projection)
    {
        string workspaceDataRoot = SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);
        string databasePath = SystemDataPaths.GetDefaultIndexDatabasePath(settings);

        if (!TryReadIndexState(databasePath, out SolutionIndexCounts counts, out bool fullRebuildRequired))
        {
            return new SourceWorkspaceSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                [],
                [],
                null,
                filter ?? string.Empty,
                "Solution index is missing. Rebuild the index to load source.");
        }

        SolutionIndexDatabase database = new(databasePath);
        bool rebuildRequired = fullRebuildRequired
            || counts.Projects == 0
            || counts.Documents == 0;
        if (rebuildRequired)
        {
            return new SourceWorkspaceSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                [],
                [],
                null,
                filter ?? string.Empty,
                "Solution index is empty or stale. Rebuild the index to load source.");
        }

        SolutionIndexStore store = new(database);
        IReadOnlyList<IndexedProjectRow> projects = FilterProjects(store.ListProjects(), settings, projection);
        IReadOnlyList<IndexedDocumentRow> documents = FilterDocuments(store.ListDocuments(), settings, projection);
        IReadOnlyList<IndexedSymbolRow> symbols = FilterSymbols(store.ListSymbols(), settings, projection);
        IReadOnlyList<SourceFileEntry> files = BuildFiles(settings.WatchedProjectFolder, documents, filter);
        IReadOnlyList<SourceTreeNode> tree = BuildTree(projects, documents, symbols, settings.WatchedProjectFolder, files);
        SourceFileEntry? selectedEntry = SelectFile(files, selectedRelativePath);
        SourceFileDocument? selectedFile = selectedEntry is null
            ? null
            : LoadDocument(selectedEntry, symbols, selectedLine);

        return new SourceWorkspaceSnapshot(
            settings.WatchedProjectFolder,
            settings.WatchedSolutionPath,
            databasePath,
            files,
            tree,
            selectedFile,
            filter ?? string.Empty,
            GetStructureMessage(files.Count, projection, filter));
    }

    public SourceWorkspaceStructureSnapshot BuildStructureSnapshot(string workspaceRoot, string? filter)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        return BuildStructureSnapshot(settings, filter, ProjectProjection.All);
    }

    public SourceWorkspaceStructureSnapshot BuildProductStructureSnapshot(string workspaceRoot, string? filter)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        return BuildStructureSnapshot(settings, filter, ProjectProjection.ProductOnly);
    }

    public SourceWorkspaceStructureSnapshot BuildTestProjectStructureSnapshot(string workspaceRoot, string? filter)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        if (settings.TestProjectPaths.Count == 0)
        {
            string databasePath = SystemDataPaths.GetDefaultIndexDatabasePath(settings);
            return new SourceWorkspaceStructureSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                0,
                [],
                "No test projects are configured in CodingServices:TestProjectPaths.");
        }

        return BuildStructureSnapshot(settings, filter, ProjectProjection.TestsOnly);
    }

    private static SourceWorkspaceStructureSnapshot BuildStructureSnapshot(
        CodingServicesSettings settings,
        string? filter,
        ProjectProjection projection)
    {
        string workspaceDataRoot = SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);
        string databasePath = SystemDataPaths.GetDefaultIndexDatabasePath(settings);

        if (!TryReadIndexState(databasePath, out SolutionIndexCounts counts, out bool fullRebuildRequired))
        {
            return new SourceWorkspaceStructureSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                0,
                [],
                "Solution index is missing. Rebuild the index to load source.");
        }

        SolutionIndexDatabase database = new(databasePath);
        bool rebuildRequired = fullRebuildRequired
            || counts.Projects == 0
            || counts.Documents == 0;
        if (rebuildRequired)
        {
            return new SourceWorkspaceStructureSnapshot(
                settings.WatchedProjectFolder,
                settings.WatchedSolutionPath,
                databasePath,
                0,
                [],
                "Solution index is empty or stale. Rebuild the index to load source.");
        }

        SolutionIndexStore store = new(database);
        IReadOnlyList<IndexedProjectRow> projects = FilterProjects(store.ListProjects(), settings, projection);
        IReadOnlyList<IndexedDocumentRow> documents = FilterDocuments(store.ListDocuments(), settings, projection);
        IReadOnlyList<IndexedSymbolRow> symbols = FilterSymbols(store.ListSymbols(), settings, projection);
        IReadOnlyList<SourceFileEntry> files = BuildFiles(settings.WatchedProjectFolder, documents, filter);
        IReadOnlyList<SourceTreeNode> tree = BuildTree(projects, documents, symbols, settings.WatchedProjectFolder, files);

        return new SourceWorkspaceStructureSnapshot(
            settings.WatchedProjectFolder,
            settings.WatchedSolutionPath,
            databasePath,
            files.Count,
            tree,
            GetStructureMessage(files.Count, projection, filter));
    }

    public async Task RebuildIndexAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
        string workspaceDataRoot = SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);
        Directory.CreateDirectory(Path.Combine(workspaceDataRoot, "data"));
        Directory.CreateDirectory(Path.Combine(workspaceDataRoot, "workflow"));
        Directory.CreateDirectory(Path.Combine(workspaceDataRoot, "reviews"));
        Directory.CreateDirectory(Path.Combine(workspaceDataRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(workspaceDataRoot, "planning"));
        Directory.CreateDirectory(SystemDataPaths.GetDefaultTaskMemoryRoot(settings));

        await new SolutionIndexRebuildService().RebuildAsync(settings, cancellationToken);
    }

    private static IReadOnlyList<IndexedProjectRow> FilterProjects(
        IReadOnlyList<IndexedProjectRow> projects,
        CodingServicesSettings settings,
        ProjectProjection projection)
    {
        return projection switch
        {
            ProjectProjection.ProductOnly => projects
                .Where(project => !IsConfiguredTestProject(project.ProjectPath, settings))
                .ToArray(),
            ProjectProjection.TestsOnly => projects
                .Where(project => IsConfiguredTestProject(project.ProjectPath, settings))
                .ToArray(),
            _ => projects
        };
    }

    private static bool TryReadIndexState(
        string databasePath,
        out SolutionIndexCounts counts,
        out bool fullRebuildRequired)
    {
        counts = new SolutionIndexCounts(0, 0, 0, 0, 0, 0);
        fullRebuildRequired = true;
        if (!File.Exists(databasePath))
        {
            return false;
        }

        try
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = Path.GetFullPath(databasePath),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using (SqliteConnection connection = new(builder.ToString()))
            {
                connection.Open();
                counts = new SolutionIndexCounts(
                    ScalarCount(connection, "projects"),
                    ScalarCount(connection, "documents"),
                    ScalarCount(connection, "symbols"),
                    ScalarCount(connection, "symbol_references"),
                    ScalarCount(connection, "call_sites"),
                    ScalarCount(connection, "symbol_relationships"));
                fullRebuildRequired = ReadFullRebuildRequired(connection);
            }

            return true;
        }
        catch (SqliteException)
        {
            counts = new SolutionIndexCounts(0, 0, 0, 0, 0, 0);
            fullRebuildRequired = true;
            return true;
        }
    }

    private static int ScalarCount(SqliteConnection connection, string table)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"select count(*) from {table};";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    private static bool ReadFullRebuildRequired(SqliteConnection connection)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select value from index_meta where key = $key;";
            command.Parameters.AddWithValue("$key", SolutionIndexDatabase.NeedsFullRebuildKey);
            object? value = command.ExecuteScalar();
            return value is string text && text == "1";
        }
    }

    private static IReadOnlyList<IndexedDocumentRow> FilterDocuments(
        IReadOnlyList<IndexedDocumentRow> documents,
        CodingServicesSettings settings,
        ProjectProjection projection)
    {
        return projection switch
        {
            ProjectProjection.ProductOnly => documents
                .Where(document => !IsConfiguredTestProject(document.ProjectPath, settings))
                .ToArray(),
            ProjectProjection.TestsOnly => documents
                .Where(document => IsConfiguredTestProject(document.ProjectPath, settings))
                .ToArray(),
            _ => documents
        };
    }

    private static IReadOnlyList<IndexedSymbolRow> FilterSymbols(
        IReadOnlyList<IndexedSymbolRow> symbols,
        CodingServicesSettings settings,
        ProjectProjection projection)
    {
        return projection switch
        {
            ProjectProjection.ProductOnly => symbols
                .Where(symbol => !IsConfiguredTestProject(symbol.ProjectPath, settings))
                .ToArray(),
            ProjectProjection.TestsOnly => symbols
                .Where(symbol => IsConfiguredTestProject(symbol.ProjectPath, settings))
                .ToArray(),
            _ => symbols
        };
    }

    private static bool IsConfiguredTestProject(string projectPath, CodingServicesSettings settings)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        return settings.TestProjectPaths.Any(testProjectPath =>
            string.Equals(
                fullProjectPath,
                Path.GetFullPath(testProjectPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStructureMessage(int fileCount, ProjectProjection projection, string? filter)
    {
        if (fileCount > 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            return "No indexed source files matched the current filter.";
        }

        return projection switch
        {
            ProjectProjection.TestsOnly => "No configured test project files were found in the index. Rebuild the index if the test project was just added.",
            ProjectProjection.ProductOnly => "No non-test source files matched the current filter.",
            _ => "No indexed source files matched the current filter."
        };
    }

    private static IReadOnlyList<SourceFileEntry> BuildFiles(
        string watchedRoot,
        IReadOnlyList<IndexedDocumentRow> documents,
        string? filter)
    {
        return documents
            .Select(document => document.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SourceFileEntry(
                NormalizePath(Path.GetRelativePath(watchedRoot, path)),
                Path.GetFullPath(path),
                GetLanguage(Path.GetExtension(path)),
                GetFileSize(path),
                File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue))
            .Where(file => string.IsNullOrWhiteSpace(filter)
                || file.RelativePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(file => GetFileRank(file.RelativePath))
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SourceTreeNode> BuildTree(
        IReadOnlyList<IndexedProjectRow> projects,
        IReadOnlyList<IndexedDocumentRow> documents,
        IReadOnlyList<IndexedSymbolRow> symbols,
        string watchedRoot,
        IReadOnlyList<SourceFileEntry> visibleFiles)
    {
        Dictionary<string, SourceFileEntry> visibleByFullPath = visibleFiles
            .ToDictionary(file => Path.GetFullPath(file.FullPath), StringComparer.OrdinalIgnoreCase);
        List<SourceTreeNode> projectNodes = [];

        foreach (IndexedProjectRow project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            MutableSourceTreeNode projectNode = new(project.Name, "project", null);
            string projectDirectory = Path.GetDirectoryName(project.ProjectPath) ?? watchedRoot;
            foreach (IndexedDocumentRow document in documents
                .Where(document => PathEquals(document.ProjectPath, project.ProjectPath))
                .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                string fullDocumentPath = Path.GetFullPath(document.FilePath);
                if (!visibleByFullPath.TryGetValue(fullDocumentPath, out SourceFileEntry? fileEntry))
                {
                    continue;
                }

                string relativeToProject = NormalizePath(Path.GetRelativePath(projectDirectory, fullDocumentPath));
                string[] segments = relativeToProject.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0 || segments.Any(IsHiddenBuildFolder))
                {
                    continue;
                }

                MutableSourceTreeNode current = projectNode;
                for (int index = 0; index < segments.Length; index++)
                {
                    bool isFile = index == segments.Length - 1;
                    current = current.GetOrAdd(
                        segments[index],
                        isFile ? "file" : "folder",
                        isFile ? fileEntry : null);
                }

                AddSymbolNodes(current, document, symbols, fileEntry);
            }

            projectNodes.Add(ConvertNode(projectNode));
        }

        return projectNodes;
    }

    private static SourceTreeNode ConvertNode(MutableSourceTreeNode node)
    {
        IReadOnlyList<SourceTreeNode> children = node.Children
            .OrderBy(child => GetTreeNodeRank(child.Kind))
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ConvertNode)
            .ToArray();

        return new SourceTreeNode(
            node.Name,
            node.Kind,
            node.File,
            node.TargetFile,
            node.Line,
            children);
    }

    private static void AddSymbolNodes(
        MutableSourceTreeNode documentNode,
        IndexedDocumentRow document,
        IReadOnlyList<IndexedSymbolRow> symbols,
        SourceFileEntry fileEntry)
    {
        Dictionary<string, MutableSourceTreeNode> typeNodes = new(StringComparer.Ordinal);
        foreach (IndexedSymbolRow symbol in symbols
            .Where(symbol => PathEquals(symbol.FilePath, document.FilePath))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal))
        {
            if (IsTypeLikeSymbol(symbol))
            {
                MutableSourceTreeNode typeNode = documentNode.GetOrAdd(
                    FormatSymbolNode(symbol),
                    "type",
                    null,
                    fileEntry,
                    Math.Max(symbol.StartLine, 1));
                typeNodes[symbol.Signature] = typeNode;
                continue;
            }

            MutableSourceTreeNode parent = documentNode;
            if (!string.IsNullOrWhiteSpace(symbol.ContainingType)
                && typeNodes.TryGetValue(symbol.ContainingType, out MutableSourceTreeNode? typeParent))
            {
                parent = typeParent;
            }

            MutableSourceTreeNode group = parent.GetOrAdd(
                FormatSymbolGroupName(symbol),
                "group",
                null,
                fileEntry,
                Math.Max(symbol.StartLine, 1));
            group.GetOrAdd(
                FormatSymbolNode(symbol),
                GetSymbolNodeKind(symbol),
                null,
                fileEntry,
                Math.Max(symbol.StartLine, 1));
        }
    }

    private static SourceFileEntry? SelectFile(IReadOnlyList<SourceFileEntry> files, string? selectedRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(selectedRelativePath))
        {
            SourceFileEntry? selected = files.FirstOrDefault(file =>
                file.RelativePath.Equals(NormalizePath(selectedRelativePath), StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return files.FirstOrDefault();
    }

    private static SourceFileDocument LoadDocument(
        SourceFileEntry entry,
        IReadOnlyList<IndexedSymbolRow> symbols,
        int? selectedLine)
    {
        string fullPath = Path.GetFullPath(entry.FullPath);
        FileInfo info = new(fullPath);
        int safeSelectedLine = Math.Max(selectedLine ?? 1, 1);
        if (!info.Exists)
        {
            return new SourceFileDocument(
                entry.RelativePath,
                fullPath,
                GetLanguage(info.Extension),
                "Indexed file no longer exists on disk.",
                safeSelectedLine,
                [],
                0);
        }

        if (info.Length > MaxReadableFileBytes)
        {
            return new SourceFileDocument(
                entry.RelativePath,
                fullPath,
                GetLanguage(info.Extension),
                $"File is too large to preview ({info.Length:n0} bytes).",
                safeSelectedLine,
                BuildOutlineFromIndex(symbols, fullPath),
                info.Length);
        }

        string text = File.ReadAllText(fullPath);
        int lineCount = Math.Max(1, text.Count(character => character == '\n') + 1);
        return new SourceFileDocument(
            entry.RelativePath,
            fullPath,
            GetLanguage(info.Extension),
            text,
            Math.Min(safeSelectedLine, lineCount),
            BuildOutlineFromIndex(symbols, fullPath),
            info.Length);
    }

    private static IReadOnlyList<SourceSymbolEntry> BuildOutlineFromIndex(
        IReadOnlyList<IndexedSymbolRow> symbols,
        string fullPath)
    {
        return symbols
            .Where(symbol => PathEquals(symbol.FilePath, fullPath))
            .OrderBy(symbol => symbol.StartLine)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
            .Take(160)
            .Select(symbol => new SourceSymbolEntry(
                string.IsNullOrWhiteSpace(symbol.Signature) ? symbol.Name : SimplifySignature(symbol.Signature),
                symbol.Kind,
                Math.Max(symbol.StartLine, 1)))
            .ToArray();
    }

    private static string SimplifySignature(string signature)
    {
        return signature
            .Replace("System.Collections.Generic.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.Tasks.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.", string.Empty, StringComparison.Ordinal)
            .Replace("System.", string.Empty, StringComparison.Ordinal);
    }

    private static long GetFileSize(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static string GetLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".razor" => "Razor",
            ".cshtml" => "Razor",
            ".csproj" => "MSBuild",
            ".sln" => "Solution",
            ".slnx" => "Solution",
            ".json" => "JSON",
            ".md" => "Markdown",
            ".xml" => "XML",
            ".xaml" => "XAML",
            ".css" => "CSS",
            ".html" => "HTML",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            _ => "Text"
        };
    }

    private static int GetFileRank(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static int GetTreeNodeRank(string kind)
    {
        return kind switch
        {
            "project" => 0,
            "folder" => 1,
            "file" => 2,
            "type" => 3,
            "group" => 4,
            "constructor" => 5,
            "method" => 5,
            "property" => 5,
            "field" => 5,
            "symbol" => 5,
            _ => 10
        };
    }

    private static string GetSymbolNodeKind(IndexedSymbolRow symbol)
    {
        if (IsConstructorSymbol(symbol))
        {
            return "constructor";
        }

        if (symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase))
        {
            return "method";
        }

        if (symbol.Kind.Equals("Property", StringComparison.OrdinalIgnoreCase))
        {
            return "property";
        }

        if (symbol.Kind.Equals("Field", StringComparison.OrdinalIgnoreCase))
        {
            return "field";
        }

        return "symbol";
    }

    private static string FormatSymbolNode(IndexedSymbolRow symbol)
    {
        string signature = FormatLocalSignature(symbol);
        return $"{signature} [{symbol.StartLine}-{symbol.EndLine}]";
    }

    private static string FormatLocalSignature(IndexedSymbolRow symbol)
    {
        if (IsTypeLikeSymbol(symbol))
        {
            return symbol.Name;
        }

        string signature = string.IsNullOrWhiteSpace(symbol.Signature)
            ? symbol.Name
            : symbol.Signature;
        string localSignature = RemoveContainingTypePrefix(signature, symbol);
        return SimplifySignatureTypes(localSignature);
    }

    private static string RemoveContainingTypePrefix(string signature, IndexedSymbolRow symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol.ContainingType))
        {
            string containingPrefix = symbol.ContainingType + ".";
            if (signature.StartsWith(containingPrefix, StringComparison.Ordinal))
            {
                return signature[containingPrefix.Length..];
            }
        }

        if (!string.IsNullOrWhiteSpace(symbol.Namespace))
        {
            string namespacePrefix = symbol.Namespace + ".";
            if (signature.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                return signature[namespacePrefix.Length..];
            }
        }

        return signature;
    }

    private static string SimplifySignatureTypes(string signature)
    {
        int parameterStart = signature.IndexOf('(', StringComparison.Ordinal);
        int parameterEnd = signature.LastIndexOf(')');
        if (parameterStart < 0 || parameterEnd <= parameterStart)
        {
            return GetUnqualifiedTypeName(signature);
        }

        string name = signature[..parameterStart];
        string parameters = signature[(parameterStart + 1)..parameterEnd];
        string suffix = signature[(parameterEnd + 1)..];
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return $"{GetUnqualifiedTypeName(name)}(){suffix}";
        }

        string[] parts = parameters.Split(',');
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = SimplifyParameter(parts[index].Trim());
        }

        return $"{GetUnqualifiedTypeName(name)}({string.Join(", ", parts)}){suffix}";
    }

    private static string SimplifyParameter(string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return parameter;
        }

        string[] tokens = parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < tokens.Length; index++)
        {
            tokens[index] = SimplifyTypeExpression(tokens[index]);
        }

        return string.Join(" ", tokens);
    }

    private static string SimplifyTypeExpression(string text)
    {
        return text
            .Replace("Microsoft.Data.Sqlite.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Collections.Generic.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.Tasks.", string.Empty, StringComparison.Ordinal)
            .Replace("System.Threading.", string.Empty, StringComparison.Ordinal)
            .Replace("System.", string.Empty, StringComparison.Ordinal);
    }

    private static string GetUnqualifiedTypeName(string name)
    {
        int genericIndex = name.IndexOf('<', StringComparison.Ordinal);
        string rootName = genericIndex >= 0
            ? name[..genericIndex]
            : name;
        int lastDot = rootName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < rootName.Length - 1)
        {
            rootName = rootName[(lastDot + 1)..];
        }

        return genericIndex >= 0
            ? rootName + name[genericIndex..]
            : rootName;
    }

    private static string FormatSymbolGroupName(IndexedSymbolRow symbol)
    {
        string access = string.IsNullOrWhiteSpace(symbol.Accessibility)
            ? "access unknown"
            : symbol.Accessibility.ToLowerInvariant();
        if (IsConstructorSymbol(symbol))
        {
            return $"{access} constructors";
        }

        if (symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase))
        {
            return $"{access} methods";
        }

        return $"{access} members";
    }

    private static bool IsTypeLikeSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("NamedType", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Class", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Struct", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Interface", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Enum", StringComparison.OrdinalIgnoreCase)
            || symbol.Kind.Equals("Record", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConstructorSymbol(IndexedSymbolRow symbol)
    {
        return symbol.Kind.Equals("Method", StringComparison.OrdinalIgnoreCase)
            && (symbol.MethodKind.Equals("Constructor", StringComparison.OrdinalIgnoreCase)
                || symbol.MethodKind.Equals("StaticConstructor", StringComparison.OrdinalIgnoreCase)
                || symbol.Name.Equals(".ctor", StringComparison.Ordinal)
                || symbol.Name.Equals(".cctor", StringComparison.Ordinal));
    }

    private static bool IsHiddenBuildFolder(string segment)
    {
        return segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("CSSourceBackups", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal enum ProjectProjection
{
    All,
    ProductOnly,
    TestsOnly
}

public sealed record SourceWorkspaceSnapshot(
    string WorkspaceRoot,
    string WatchedSolutionPath,
    string IndexDatabasePath,
    IReadOnlyList<SourceFileEntry> Files,
    IReadOnlyList<SourceTreeNode> Tree,
    SourceFileDocument? SelectedFile,
    string Filter,
    string Message)
{
    public static SourceWorkspaceSnapshot Empty(string message)
    {
        return new SourceWorkspaceSnapshot(string.Empty, string.Empty, string.Empty, [], [], null, string.Empty, message);
    }
}

public sealed record SourceWorkspaceStructureSnapshot(
    string WorkspaceRoot,
    string WatchedSolutionPath,
    string IndexDatabasePath,
    int FileCount,
    IReadOnlyList<SourceTreeNode> Tree,
    string Message);

public sealed record SourceFileEntry(
    string RelativePath,
    string FullPath,
    string Language,
    long Size,
    DateTime LastWriteTime);

public sealed record SourceFileDocument(
    string RelativePath,
    string FullPath,
    string Language,
    string Text,
    int SelectedLine,
    IReadOnlyList<SourceSymbolEntry> Outline,
    long Size);

public sealed record SourceSelection(
    string RelativePath,
    int Line);

public sealed record SourceSymbolEntry(
    string Name,
    string Kind,
    int Line);

public sealed record SourceTreeNode(
    string Name,
    string Kind,
    SourceFileEntry? File,
    SourceFileEntry? TargetFile,
    int Line,
    IReadOnlyList<SourceTreeNode> Children);

internal sealed class MutableSourceTreeNode
{
    private readonly Dictionary<string, MutableSourceTreeNode> children = new(StringComparer.OrdinalIgnoreCase);

    public MutableSourceTreeNode(string name, string kind, SourceFileEntry? file)
    {
        Name = name;
        Kind = kind;
        File = file;
    }

    public string Name { get; }

    public string Kind { get; private set; }

    public SourceFileEntry? File { get; private set; }

    public SourceFileEntry? TargetFile { get; private set; }

    public int Line { get; private set; } = 1;

    public IEnumerable<MutableSourceTreeNode> Children => children.Values;

    public MutableSourceTreeNode GetOrAdd(
        string name,
        string kind,
        SourceFileEntry? file,
        SourceFileEntry? targetFile = null,
        int line = 1)
    {
        if (children.TryGetValue(name, out MutableSourceTreeNode? child))
        {
            if (file is not null)
            {
                child.Kind = kind;
                child.File = file;
            }

            if (targetFile is not null)
            {
                child.TargetFile = targetFile;
                child.Line = line;
            }

            return child;
        }

        child = new MutableSourceTreeNode(name, kind, file);
        child.TargetFile = targetFile;
        child.Line = line;
        children.Add(name, child);
        return child;
    }
}
