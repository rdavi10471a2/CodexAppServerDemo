using System.Text.Json;
using Microsoft.Build.Framework;

namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed class ProjectCountLogger : ILogger
{
    public const string SummaryJsonParameterName = "summaryJson";
    public const string ConsoleParameterName = "console";

    private int startedProjectCount;
    private int succeededProjectCount;
    private int warningCount;
    private int errorCount;
    private string? summaryJsonPath;
    private bool writeConsole = true;

    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Minimal;

    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        Configure(Parameters);
        eventSource.ProjectStarted += OnProjectStarted;
        eventSource.ProjectFinished += OnProjectFinished;
        eventSource.WarningRaised += OnWarningRaised;
        eventSource.ErrorRaised += OnErrorRaised;
    }

    public void Shutdown()
    {
        BuildProjectCounts counts = CaptureCounts();
        if (!string.IsNullOrWhiteSpace(summaryJsonPath))
        {
            WriteSummaryJson(summaryJsonPath, counts);
        }

        if (writeConsole)
        {
            Console.WriteLine(counts.ToDisplayText());
        }
    }

    public void Configure(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return;
        }

        foreach (string parameter in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = parameter.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                summaryJsonPath ??= parameter;
                continue;
            }

            string name = parameter[..separatorIndex].Trim();
            string value = parameter[(separatorIndex + 1)..].Trim();
            if (string.Equals(name, SummaryJsonParameterName, StringComparison.OrdinalIgnoreCase))
            {
                summaryJsonPath = value;
                continue;
            }

            if (string.Equals(name, ConsoleParameterName, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out bool parsedConsole))
            {
                writeConsole = parsedConsole;
            }
        }
    }

    public void RecordProjectStarted()
    {
        startedProjectCount++;
    }

    public void RecordProjectFinished(bool succeeded)
    {
        if (succeeded)
        {
            succeededProjectCount++;
        }
    }

    public void RecordWarning()
    {
        warningCount++;
    }

    public void RecordError()
    {
        errorCount++;
    }

    public BuildProjectCounts CaptureCounts()
    {
        int failedProjects = Math.Max(0, startedProjectCount - succeededProjectCount);
        return new BuildProjectCounts(
            startedProjectCount,
            succeededProjectCount,
            failedProjects,
            warningCount,
            errorCount);
    }

    private static void WriteSummaryJson(string path, BuildProjectCounts counts)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(counts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void OnProjectStarted(object sender, ProjectStartedEventArgs e)
    {
        RecordProjectStarted();
    }

    private void OnProjectFinished(object sender, ProjectFinishedEventArgs e)
    {
        RecordProjectFinished(e.Succeeded);
    }

    private void OnWarningRaised(object sender, BuildWarningEventArgs e)
    {
        RecordWarning();
    }

    private void OnErrorRaised(object sender, BuildErrorEventArgs e)
    {
        RecordError();
    }
}
