using CodexAppServerBlazor.AICodingServices.MSBuild;

namespace CodexAppServerBlazor.AICodingServices.Workflow;

public sealed class PreMergeValidationResult
{
    public string Status { get; set; } = string.Empty;

    public bool IsError { get; set; }

    public int DiagnosticCount { get; set; }

    public string[] Diagnostics { get; set; } = [];

    public string ValidationWorkspacePath { get; set; } = string.Empty;

    public GovernedCommandReductionResult[] CommandReductions { get; set; } = [];

    public BuildProjectSummary[] BuildSummaries { get; set; } = [];

    public string Message { get; set; } = string.Empty;
}
