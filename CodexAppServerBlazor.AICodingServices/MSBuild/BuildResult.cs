namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed record BuildResult(
    BuildValidationPhase Phase,
    int ExitCode,
    bool TimedOut,
    BuildProjectCounts Counts,
    string CommandText,
    string SummaryJsonPath,
    string RawOutputPath,
    string StandardOutputPath,
    string StandardErrorPath,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    TimeSpan Duration,
    int RawOutputCharacters)
{
    public bool Failed => TimedOut || ExitCode != 0;

    public BuildProjectSummary ToSummary()
    {
        return new BuildProjectSummary(Phase, Counts);
    }
}