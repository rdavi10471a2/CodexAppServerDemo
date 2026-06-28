using System.Diagnostics;
using System.Xml.Linq;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.MSBuild;

namespace CodexAppServerBlazor.AICodingServices.Workflow;

public sealed class PreMergeValidationService
{
    private const string DisableDefaultScopedCssItemsProperty = "/p:EnableDefaultScopedCssItems=false";
    public PreMergeValidationResult ValidateStagedOverlay(
        StagedEditRecord record,
        IReadOnlyList<StagedEditRecord> stagedOverlayRecords)
    {
        StagedEditRecord[] overlayRecords = NormalizeOverlayRecords(record, stagedOverlayRecords);
        foreach (StagedEditRecord overlayRecord in overlayRecords)
        {
            PreMergeValidationResult? hashFailure = ValidateStagedRecordHash(overlayRecord);
            if (hashFailure is not null)
            {
                return hashFailure;
            }
        }

        return new PreMergeValidationResult
        {
            Status = overlayRecords.Length <= 1 ? "staged-file-ready" : "planned-staged-overlay-ready",
            IsError = false,
            DiagnosticCount = 0,
            Diagnostics = [],
            Message = overlayRecords.Length <= 1
                ? "Staged file is ready for WinMerge review. Build/index validation is deferred until accept."
                : $"Planned staged overlay is ready for WinMerge review with {overlayRecords.Length} staged files. Build/index validation is deferred until the planned files are accepted."
        };
    }

    public PreMergeValidationResult Validate(CodingServicesSettings settings, StagedEditRecord record)
    {
        return Validate(settings, record, [record]);
    }

    public PreMergeValidationResult Validate(
        CodingServicesSettings settings,
        StagedEditRecord record,
        IReadOnlyList<StagedEditRecord> stagedOverlayRecords)
    {
        StagedEditRecord[] overlayRecords = NormalizeOverlayRecords(record, stagedOverlayRecords);
        foreach (StagedEditRecord overlayRecord in overlayRecords)
        {
            PreMergeValidationResult? hashFailure = ValidateStagedRecordHash(overlayRecord);
            if (hashFailure is not null)
            {
                return hashFailure;
            }
        }

        if (!File.Exists(settings.WatchedSolutionPath))
        {
            return new PreMergeValidationResult
            {
                Status = "missing-solution",
                IsError = true,
                Message = "Pre-merge validation failed because the watched solution file is missing."
            };
        }

        string validationWorkspaceRoot = Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "validation",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..42]);
        string sourceRoot = settings.WatchedProjectFolder;
        try
        {
            Directory.CreateDirectory(validationWorkspaceRoot);
            string overlayRoot = Path.Combine(validationWorkspaceRoot, "overlay");
            string artifactsPath = Path.Combine(validationWorkspaceRoot, "artifacts");
            string overlayTargetsPath = Path.Combine(validationWorkspaceRoot, "Overlay.targets");
            string[] projectPaths = ResolveValidationProjectPaths(settings.WatchedSolutionPath, sourceRoot);
            OverlayItem[] overlayItems = CreateOverlayItems(overlayRecords, projectPaths, overlayRoot);
            if (overlayItems.Length == 0)
            {
                return new PreMergeValidationResult
                {
                    Status = "passed",
                    IsError = false,
                    DiagnosticCount = 0,
                    Diagnostics = [],
                    ValidationWorkspacePath = validationWorkspaceRoot,
                    Message = overlayRecords.Length <= 1
                        ? "Pre-merge single-file validation skipped because no MSBuild item owns the staged file."
                        : $"Pre-merge session overlay validation skipped because no MSBuild item owns the {overlayRecords.Length} staged files."
                };
            }

            WriteOverlayTargets(overlayTargetsPath, overlayItems);
            string[] buildTargets = overlayItems
                .Select(item => item.ProjectPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ValidationBuildResult build = RunValidationBuilds(buildTargets, sourceRoot, artifactsPath, overlayTargetsPath, settings.RuntimeRoot);
            string[] errorDiagnostics = ExtractGovernedBuildErrors(build);
            if (build.Failed && errorDiagnostics.Length == 0)
            {
                errorDiagnostics = [$"dotnet build exited with code {build.ExitCode} but did not emit parseable error diagnostics."];
            }

            return new PreMergeValidationResult
            {
                Status = build.TimedOut ? "timeout" : build.Failed ? "failed" : "passed",
                IsError = build.Failed,
                DiagnosticCount = errorDiagnostics.Length,
                Diagnostics = errorDiagnostics,
                ValidationWorkspacePath = validationWorkspaceRoot,
                CommandReductions = build.CommandReductions,
                BuildSummaries = build.BuildSummaries,
                Message = build.TimedOut
                    ? CreateValidationMessage("timed out", overlayRecords.Length, buildTargets.Length)
                    : build.Failed
                        ? CreateValidationMessage("failed", overlayRecords.Length, buildTargets.Length)
                        : CreateValidationMessage("passed", overlayRecords.Length, buildTargets.Length)
            };
        }
        catch (Exception ex)
        {
            return new PreMergeValidationResult
            {
                Status = "failed",
                IsError = true,
                DiagnosticCount = 1,
                Diagnostics = [ex.Message],
                ValidationWorkspacePath = validationWorkspaceRoot,
                Message = CreateValidationMessage("failed", overlayRecords.Length)
            };
        }
    }

    private static StagedEditRecord[] NormalizeOverlayRecords(
        StagedEditRecord currentRecord,
        IReadOnlyList<StagedEditRecord> stagedOverlayRecords)
    {
        Dictionary<string, StagedEditRecord> recordsByPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (StagedEditRecord overlayRecord in stagedOverlayRecords)
        {
            if (!string.IsNullOrWhiteSpace(overlayRecord.WatchedFilePath))
            {
                recordsByPath[Path.GetFullPath(overlayRecord.WatchedFilePath)] = overlayRecord;
            }
        }

        recordsByPath[Path.GetFullPath(currentRecord.WatchedFilePath)] = currentRecord;
        return recordsByPath.Values
            .OrderBy(overlayRecord => overlayRecord.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PreMergeValidationResult? ValidateStagedRecordHash(StagedEditRecord record)
    {
        if (!File.Exists(record.StagedFilePath))
        {
            return new PreMergeValidationResult
            {
                Status = "missing-staged-file",
                IsError = true,
                Message = "Validation failed because a staged overlay file is missing."
            };
        }

        string currentStagedHash = FileHash.Compute(record.StagedFilePath);
        if (!currentStagedHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase))
        {
            return new PreMergeValidationResult
            {
                Status = "staged-hash-mismatch",
                IsError = true,
                DiagnosticCount = 1,
                Diagnostics = ["A staged overlay file changed after staging. Edit the Working file and run edit stage again."],
                Message = "Validation failed because a staged overlay file no longer matches its recorded hash."
            };
        }

        return null;
    }

    private static string CreateValidationMessage(string outcome, int overlayFileCount, int buildTargetCount = 1)
    {
        string targetLabel = buildTargetCount == 1 ? "real-tree overlay project build" : $"{buildTargetCount} real-tree overlay project builds";
        if (overlayFileCount <= 1)
        {
            return $"Pre-merge single-file {targetLabel} {outcome}.";
        }

        return $"Pre-merge session overlay {targetLabel} {outcome} with {overlayFileCount} staged files.";
    }

    private static ValidationBuildResult RunValidationBuilds(
        string[] buildTargets,
        string sourceRoot,
        string artifactsPath,
        string overlayTargetsPath,
        string runtimeRoot)
    {
        List<string> outputBlocks = [];
        List<GovernedCommandReductionResult> commandReductions = [];
        List<BuildDiagnostic> buildDiagnostics = [];
        List<BuildProjectSummary> buildSummaries = [];
        int lastExitCode = 0;
        bool timedOut = false;
        GovernedCommandOutputReducer reducer = new();
        GovernedCommandArtifactWriter artifactWriter = new(runtimeRoot);
        IBuildRunner buildRunner = new DotNetBuildRunner();
        foreach (string buildTarget in buildTargets)
        {
            string[] restoreArguments =
            [
                "restore",
                buildTarget,
                "--artifacts-path",
                artifactsPath,
                "/p:NuGetAudit=false",
                DisableDefaultScopedCssItemsProperty,
                $"/p:CustomAfterMicrosoftCommonTargets={overlayTargetsPath}"
            ];
            ProcessResult restore = RunProcess(
                "dotnet",
                restoreArguments,
                sourceRoot,
                TimeSpan.FromMinutes(3));
            GovernedCommandReductionResult restoreReduction = ReduceProcessResult(
                reducer,
                artifactWriter,
                "dotnet",
                restoreArguments,
                sourceRoot,
                restore);
            commandReductions.Add(restoreReduction);
            lastExitCode = restore.ExitCode;
            timedOut = timedOut || restore.TimedOut;
            outputBlocks.Add(restoreReduction.VisibleOutput);
            if (restore.TimedOut || restore.ExitCode != 0)
            {
                break;
            }

            string[] additionalBuildArguments =
            [
                "--artifacts-path",
                artifactsPath,
                "/p:UseAppHost=false",
                "/p:NuGetAudit=false",
                DisableDefaultScopedCssItemsProperty,
                $"/p:CustomAfterMicrosoftCommonTargets={overlayTargetsPath}"
            ];
            BuildResult build = buildRunner.Run(
                new BuildRequest(
                    buildTarget,
                    BuildValidationPhase.Overlay,
                    sourceRoot,
                    Path.Combine(artifactsPath, "build"),
                    additionalBuildArguments),
                TimeSpan.FromMinutes(3));
            GovernedCommandReductionResult buildReduction = CreateBuildReduction(build);
            commandReductions.Add(buildReduction);
            buildSummaries.Add(build.ToSummary());
            buildDiagnostics.AddRange(build.Diagnostics);
            lastExitCode = build.ExitCode;
            timedOut = timedOut || build.TimedOut;
            outputBlocks.Add(buildReduction.VisibleOutput);
            if (build.TimedOut || build.ExitCode != 0)
            {
                break;
            }
        }

        return new ValidationBuildResult(
            lastExitCode,
            timedOut,
            timedOut || lastExitCode != 0,
            string.Join(Environment.NewLine, outputBlocks),
            commandReductions.ToArray(),
            buildDiagnostics.ToArray(),
            buildSummaries.ToArray());
    }
    private static string[] ResolveValidationProjectPaths(string watchedSolutionPath, string sourceRoot)
    {
        string extension = Path.GetExtension(watchedSolutionPath);
        if (extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
        {
            return [Path.GetFullPath(watchedSolutionPath)];
        }

        string[] projectPaths = ReadSlnxProjectPaths(watchedSolutionPath)
            .Select(path => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(watchedSolutionPath) ?? sourceRoot, path)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (projectPaths.Length > 0)
        {
            return projectPaths;
        }

        return Directory.EnumerateFiles(sourceRoot, "*.*proj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsSkippedValidationDirectory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OverlayItem[] CreateOverlayItems(
        StagedEditRecord[] overlayRecords,
        IReadOnlyList<string> projectPaths,
        string overlayRoot)
    {
        List<OverlayItem> items = [];
        foreach (StagedEditRecord record in overlayRecords)
        {
            string watchedPath = Path.GetFullPath(record.WatchedFilePath);
            string? itemType = DetermineOverlayItemType(watchedPath);
            if (itemType is null)
            {
                continue;
            }

            string? projectPath = FindContainingProject(watchedPath, projectPaths);
            if (projectPath is null)
            {
                continue;
            }

            string overlayPath = Path.GetFullPath(Path.Combine(overlayRoot, record.RelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(overlayPath) ?? overlayRoot);
            File.Copy(record.StagedFilePath, overlayPath, overwrite: true);
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
            string linkPath = Path.GetRelativePath(projectDirectory, watchedPath);
            items.Add(new OverlayItem(projectPath, watchedPath, overlayPath, itemType, linkPath));
        }

        return items.ToArray();
    }

    private static string? DetermineOverlayItemType(string watchedPath)
    {
        string fileName = Path.GetFileName(watchedPath);
        string extension = Path.GetExtension(watchedPath);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Compile";
        }

        if (extension.Equals(".resx", StringComparison.OrdinalIgnoreCase))
        {
            return "EmbeddedResource";
        }

        if (fileName.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".config", StringComparison.OrdinalIgnoreCase))
        {
            return "Content";
        }

        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "Content";
    }

    private static void WriteOverlayTargets(string overlayTargetsPath, IReadOnlyList<OverlayItem> overlayItems)
    {
        string[] removableItemTypes =
        [
            "Compile",
            "Content",
            "None",
            "EmbeddedResource",
            "AdditionalFiles",
            "RazorComponent",
            "RazorGenerate",
            "UpToDateCheckInput"
        ];
        XElement project = new("Project");
        foreach (OverlayItem item in overlayItems)
        {
            XElement itemGroup = new(
                "ItemGroup",
                new XAttribute("Condition", $"'$(MSBuildProjectFullPath)' == '{item.ProjectPath}'"));
            foreach (string itemType in removableItemTypes)
            {
                itemGroup.Add(new XElement(itemType, new XAttribute("Remove", item.WatchedPath)));
            }

            itemGroup.Add(new XElement(
                item.ItemType,
                new XAttribute("Include", item.OverlayPath),
                new XElement("Link", item.LinkPath)));
            project.Add(itemGroup);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(overlayTargetsPath) ?? Directory.GetCurrentDirectory());
        XDocument document = new(new XDeclaration("1.0", "utf-8", null), project);
        document.Save(overlayTargetsPath);
    }

    private static IEnumerable<string> ReadSlnxProjectPaths(string solutionPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(solutionPath);
        }
        catch
        {
            yield break;
        }

        foreach (XElement project in document.Descendants().Where(element => element.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase)))
        {
            string? path = project.Attribute("Path")?.Value;
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static string? FindContainingProject(string filePath, IReadOnlyList<string> projectPaths)
    {
        string? bestProject = null;
        int bestLength = -1;
        foreach (string projectPath in projectPaths)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
            if (IsPathUnderAny(filePath, [projectDirectory]) && projectDirectory.Length > bestLength)
            {
                bestProject = projectPath;
                bestLength = projectDirectory.Length;
            }
        }

        return bestProject;
    }

    private static bool IsPathUnderAny(string path, IReadOnlyList<string> roots)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot = Path.GetFullPath(root);
            if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSkippedValidationDirectory(string directoryName)
    {
        return directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("runtime", StringComparison.OrdinalIgnoreCase);
    }

    private static GovernedCommandReductionResult CreateBuildReduction(BuildResult build)
    {
        string visibleOutput = build.Counts.ToDisplayText();
        GovernedCommandDiagnostic[] diagnostics = build.Diagnostics.Select(ToGovernedDiagnostic).ToArray();
        int visibleLineCount = visibleOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        return new GovernedCommandReductionResult(
            GovernedCommandKind.Build,
            GovernedCommandOutputMode.Diagnostics,
            build.ExitCode,
            (long)build.Duration.TotalMilliseconds,
            build.RawOutputCharacters,
            visibleOutput.Length,
            visibleLineCount,
            Truncated: false,
            visibleOutput,
            build.RawOutputPath,
            diagnostics,
            Array.Empty<GovernedCommandWarning>());
    }

    private static GovernedCommandDiagnostic ToGovernedDiagnostic(BuildDiagnostic diagnostic)
    {
        return new GovernedCommandDiagnostic(
            diagnostic.FilePath,
            diagnostic.Line,
            diagnostic.Column,
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message,
            diagnostic.ProjectPath);
    }

    private static string FormatDiagnostic(BuildDiagnostic diagnostic)
    {
        string location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? string.Empty
            : $" ({diagnostic.FilePath}:{diagnostic.Line})";
        return $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}{location}";
    }
    private static string[] ExtractGovernedBuildErrors(ValidationBuildResult build)
    {
        string[] governedDiagnostics = build.CommandReductions
            .SelectMany(reduction => reduction.Diagnostics)
            .Where(diagnostic => diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            .Select(FormatDiagnostic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
        if (governedDiagnostics.Length > 0)
        {
            return governedDiagnostics;
        }

        return ExtractBuildErrors(build.Output);
    }

    private static string FormatDiagnostic(GovernedCommandDiagnostic diagnostic)
    {
        string location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? string.Empty
            : $" ({diagnostic.FilePath}:{diagnostic.Line})";
        return $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}{location}";
    }

    private static GovernedCommandReductionResult ReduceProcessResult(
        GovernedCommandOutputReducer reducer,
        GovernedCommandArtifactWriter artifactWriter,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ProcessResult process)
    {
        GovernedCommandRequest request = new(CreateCommandText(fileName, arguments), workingDirectory);
        GovernedCommandRawResult raw = new(
            process.StandardOutput,
            process.StandardError,
            process.ExitCode,
            process.Duration);
        GovernedCommandKind kind = GovernedCommandOutputReducer.Classify(request.Command);
        string artifactPath = artifactWriter.WriteArtifact(request, raw, kind);
        return reducer.Reduce(request, raw, artifactPath);
    }

    private static string CreateCommandText(string fileName, IReadOnlyList<string> arguments)
    {
        return fileName + " " + string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "\"\"";
        }

        if (!argument.Any(char.IsWhiteSpace) && !argument.Contains('"', StringComparison.Ordinal))
        {
            return argument;
        }

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static ProcessResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        Process process = new();
        Stopwatch stopwatch = new();
        try
        {
            process.StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            stopwatch.Start();
            process.Start();
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(timeout);
            stopwatch.Stop();
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }

            return new ProcessResult(
                exited ? process.ExitCode : -1,
                !exited,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult(),
                stopwatch.Elapsed);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string[] ExtractBuildErrors(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private sealed record OverlayItem(
        string ProjectPath,
        string WatchedPath,
        string OverlayPath,
        string ItemType,
        string LinkPath);

    private sealed record ProcessResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError,
        TimeSpan Duration);

    private sealed record ValidationBuildResult(
        int ExitCode,
        bool TimedOut,
        bool Failed,
        string Output,
        GovernedCommandReductionResult[] CommandReductions,
        BuildDiagnostic[] BuildDiagnostics,
        BuildProjectSummary[] BuildSummaries);
}
