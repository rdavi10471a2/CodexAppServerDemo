namespace CodexAppServerBlazor.AICodingServices.Workflow;

public enum GovernedCommandKind
{
    Unknown,
    ProcessStatus,
    Build,
    Test,
    Search,
    FileRead,
    RuntimeDebug,
    Git,
    Other
}

public enum GovernedCommandOutputMode
{
    Summary,
    Diagnostics,
    Matches,
    Full
}

public enum GovernedCommandWarningCode
{
    RuntimeWorkingRead,
    OutputTruncated,
    FullOutputArtifactMissing
}

public sealed record GovernedCommandRequest(
    string Command,
    string? WorkingDirectory = null);

public sealed record GovernedCommandRawResult(
    string StandardOutput,
    string StandardError,
    int ExitCode,
    TimeSpan Duration);

public sealed record GovernedCommandPolicyOptions(
    int MaxVisibleCharacters = 4000,
    int MaxSearchMatches = 40,
    int ContextLineCount = 12);

public sealed record GovernedCommandDiagnostic(
    string? FilePath,
    int? Line,
    int? Column,
    string Severity,
    string? Code,
    string Message,
    string? ProjectPath);

public sealed record GovernedCommandWarning(
    GovernedCommandWarningCode Code,
    string Message);

public sealed record GovernedCommandReductionResult(
    GovernedCommandKind Kind,
    GovernedCommandOutputMode OutputMode,
    int ExitCode,
    long DurationMilliseconds,
    int RawOutputCharacters,
    int VisibleOutputCharacters,
    int TotalLineCount,
    bool Truncated,
    string VisibleOutput,
    string? FullOutputArtifactPath,
    IReadOnlyList<GovernedCommandDiagnostic> Diagnostics,
    IReadOnlyList<GovernedCommandWarning> Warnings);
