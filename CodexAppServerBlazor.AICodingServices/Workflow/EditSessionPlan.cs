namespace CodexAppServerBlazor.AICodingServices.Workflow;

public sealed class EditSessionPlan
{
    public string SessionId { get; set; } = string.Empty;

    public List<string> DeclaredWatchedFilePaths { get; set; } = [];

    public List<string> DeclaredRelativePaths { get; set; } = [];

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;
}
