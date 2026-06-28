namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed record BuildProjectCounts(
    int TotalProjectsCompiled,
    int TotalSucceeded,
    int TotalFailed,
    int WarningCount,
    int ErrorCount)
{
    public string ToDisplayText()
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Total Projects Compiled: {TotalProjectsCompiled}",
                $"Total Succeeded: {TotalSucceeded}",
                $"Total Failed: {TotalFailed}",
                $"Warnings: {WarningCount}",
                $"Errors: {ErrorCount}"
            ]);
    }
}
