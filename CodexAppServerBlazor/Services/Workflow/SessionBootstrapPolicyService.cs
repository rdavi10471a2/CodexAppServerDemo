namespace CodexAppServerBlazor.Services.Workflow;

public sealed class SessionBootstrapPolicyService
{
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment hostEnvironment;

    public SessionBootstrapPolicyService(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        this.configuration = configuration;
        this.hostEnvironment = hostEnvironment;
    }

    public SessionBootstrapPolicy LoadPolicy()
    {
        string? configuredPath = configuration["WorkflowPolicy:SessionBootstrapPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new SessionBootstrapPolicy(null, null, "Session bootstrap policy is not configured.");
        }

        string fullPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, configuredPath));
        if (!File.Exists(fullPath))
        {
            return new SessionBootstrapPolicy(fullPath, null, "Session bootstrap policy file is missing.");
        }

        string text = File.ReadAllText(fullPath).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SessionBootstrapPolicy(fullPath, null, "Session bootstrap policy file is empty.");
        }

        return new SessionBootstrapPolicy(fullPath, text, "Session bootstrap policy loaded.");
    }
}

public sealed record SessionBootstrapPolicy(
    string? SourcePath,
    string? PromptText,
    string Status)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(PromptText);
}
