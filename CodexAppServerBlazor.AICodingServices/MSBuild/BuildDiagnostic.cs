namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed record BuildDiagnostic(
    string? FilePath,
    int? Line,
    int? Column,
    string Severity,
    string? Code,
    string Message,
    string? ProjectPath);