using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed class DotNetBuildRunner : IBuildRunner
{
    private const string LoggerTypeName = "CodexAppServerBlazor.AICodingServices.MSBuild.ProjectCountLogger";
    private const string SummaryJsonParameterName = "summaryJson";
    private const string ConsoleParameterName = "console";

    public BuildResult Run(BuildRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(request.ArtifactRoot);

        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..42];
        string targetName = SanitizeFileName(Path.GetFileNameWithoutExtension(request.TargetPath));
        string summaryJsonPath = Path.Combine(request.ArtifactRoot, $"{targetName}-{stamp}.summary.json");
        string standardOutputPath = Path.Combine(request.ArtifactRoot, $"{targetName}-{stamp}.stdout.log");
        string standardErrorPath = Path.Combine(request.ArtifactRoot, $"{targetName}-{stamp}.stderr.log");
        string rawOutputPath = Path.Combine(request.ArtifactRoot, $"{targetName}-{stamp}.raw.log");

        List<string> arguments =
        [
            "build",
            request.TargetPath,
            "--no-restore",
            "--nologo",
            "-v:quiet",
            "-noconsolelogger",
            "/nodeReuse:false"
        ];
        arguments.AddRange(request.AdditionalArguments);
        string loggerAssemblyPath = typeof(DotNetBuildRunner).Assembly.Location;
        arguments.Add($"/logger:{LoggerTypeName},{loggerAssemblyPath};{SummaryJsonParameterName}={summaryJsonPath};{ConsoleParameterName}=true");

        string commandText = CreateCommandText("dotnet", arguments);
        ProcessResult process = RunProcess("dotnet", arguments, request.WorkingDirectory, timeout);
        File.WriteAllText(standardOutputPath, process.StandardOutput);
        File.WriteAllText(standardErrorPath, process.StandardError);
        string rawOutput = string.Concat(
            process.StandardOutput,
            string.IsNullOrWhiteSpace(process.StandardError) ? string.Empty : Environment.NewLine,
            process.StandardError);
        File.WriteAllText(rawOutputPath, rawOutput);

        BuildProjectCounts counts = ReadCounts(summaryJsonPath);
        BuildDiagnostic[] diagnostics = ExtractDiagnostics(process.StandardOutput, process.StandardError);
        return new BuildResult(
            request.Phase,
            process.ExitCode,
            process.TimedOut,
            counts,
            commandText,
            summaryJsonPath,
            rawOutputPath,
            standardOutputPath,
            standardErrorPath,
            diagnostics,
            process.Duration,
            rawOutput.Length);
    }

    private static BuildProjectCounts ReadCounts(string summaryJsonPath)
    {
        if (!File.Exists(summaryJsonPath))
        {
            return new BuildProjectCounts(0, 0, 0, 0, 0);
        }

        string json = File.ReadAllText(summaryJsonPath);
        return JsonSerializer.Deserialize<BuildProjectCounts>(json) ?? new BuildProjectCounts(0, 0, 0, 0, 0);
    }

    private static BuildDiagnostic[] ExtractDiagnostics(string standardOutput, string standardError)
    {
        string combined = string.Concat(standardOutput, Environment.NewLine, standardError);
        return combined
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseDiagnostic)
            .Where(diagnostic => diagnostic is not null)
            .Select(diagnostic => diagnostic!)
            .DistinctBy(diagnostic => new
            {
                diagnostic.FilePath,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Severity,
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.ProjectPath
            })
            .Take(100)
            .ToArray();
    }

    private static BuildDiagnostic? ParseDiagnostic(string line)
    {
        Match match = Regex.Match(
            line,
            @"^(?<file>.*?)(\((?<line>\d+),(?<column>\d+)\))?:\s*(?<severity>error|warning)\s+(?<code>[^:]+):\s*(?<message>.*?)(\s+\[(?<project>.*)\])?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        int? lineNumber = int.TryParse(match.Groups["line"].Value, out int parsedLine) ? parsedLine : null;
        int? column = int.TryParse(match.Groups["column"].Value, out int parsedColumn) ? parsedColumn : null;
        string? projectPath = match.Groups["project"].Success ? match.Groups["project"].Value : null;
        return new BuildDiagnostic(
            match.Groups["file"].Value,
            lineNumber,
            column,
            match.Groups["severity"].Value.ToLowerInvariant(),
            match.Groups["code"].Value,
            match.Groups["message"].Value,
            projectPath);
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

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "build" : sanitized;
    }

    private sealed record ProcessResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError,
        TimeSpan Duration);
}