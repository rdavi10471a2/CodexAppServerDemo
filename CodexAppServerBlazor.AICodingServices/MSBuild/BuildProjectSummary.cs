namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed record BuildProjectSummary(
    BuildValidationPhase Phase,
    BuildProjectCounts Counts);
