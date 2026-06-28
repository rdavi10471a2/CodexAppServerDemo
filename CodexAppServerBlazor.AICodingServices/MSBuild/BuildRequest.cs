namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public sealed record BuildRequest(
    string TargetPath,
    BuildValidationPhase Phase,
    string WorkingDirectory,
    string ArtifactRoot,
    IReadOnlyList<string> AdditionalArguments);