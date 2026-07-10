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
        string? bootstrapPath = configuration["WorkflowPolicy:SessionBootstrapPath"];
        string? hostAgentsPath = configuration["WorkflowPolicy:HostAgentsPath"];

        LoadedPolicyText bootstrap = LoadOptionalText(
            bootstrapPath,
            defaultRelativePath: null,
            label: "session bootstrap");
        LoadedPolicyText hostAgents = LoadOptionalText(
            hostAgentsPath,
            defaultRelativePath: Path.Combine("..", "AGENTS.md"),
            label: "host AGENTS");

        List<string> sections = [];
        List<string> sourcePaths = [];
        List<string> statusParts = [];

        if (!string.IsNullOrWhiteSpace(hostAgents.Text))
        {
            sections.Add(
                "Effective Coding Services host governance for this session:" + Environment.NewLine + Environment.NewLine +
                "- Apply the following host AGENTS.md rules as active session policy for this Coding Services session." + Environment.NewLine +
                "- These host rules remain in effect even when the selected workspace is outside the Coding Services host repository." + Environment.NewLine +
                "- Do not claim the host governance is unavailable merely because the selected workspace has no local AGENTS.md file." + Environment.NewLine +
                "- Treat this attached host policy as session-level governance, not as optional background reference text." + Environment.NewLine + Environment.NewLine +
                "Coding Services host AGENTS.md policy:" + Environment.NewLine + Environment.NewLine +
                hostAgents.Text);
            if (!string.IsNullOrWhiteSpace(hostAgents.SourcePath))
            {
                sourcePaths.Add(hostAgents.SourcePath);
            }

            statusParts.Add("Host AGENTS loaded.");
        }
        else
        {
            statusParts.Add(hostAgents.Status);
        }

        if (!string.IsNullOrWhiteSpace(bootstrap.Text))
        {
            sections.Add(bootstrap.Text);
            if (!string.IsNullOrWhiteSpace(bootstrap.SourcePath))
            {
                sourcePaths.Add(bootstrap.SourcePath);
            }

            statusParts.Add("Session bootstrap loaded.");
        }
        else
        {
            statusParts.Add(bootstrap.Status);
        }

        string? prompt = sections.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
        string? sourcePath = sourcePaths.Count == 0
            ? null
            : string.Join(" | ", sourcePaths);
        string status = prompt is null
            ? string.Join(" ", statusParts)
            : string.Join(" ", statusParts.Where(part => !string.IsNullOrWhiteSpace(part)));

        return new SessionBootstrapPolicy(sourcePath, prompt, status);
    }

    private LoadedPolicyText LoadOptionalText(string? configuredPath, string? defaultRelativePath, string label)
    {
        string? resolvedPath = ResolveOptionalPath(configuredPath, defaultRelativePath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return new LoadedPolicyText(null, null, $"{label} is not configured.");
        }

        if (!File.Exists(resolvedPath))
        {
            return new LoadedPolicyText(resolvedPath, null, $"{label} file is missing.");
        }

        string text = File.ReadAllText(resolvedPath).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LoadedPolicyText(resolvedPath, null, $"{label} file is empty.");
        }

        return new LoadedPolicyText(resolvedPath, text, $"{label} loaded.");
    }

    private string? ResolveOptionalPath(string? configuredPath, string? defaultRelativePath)
    {
        string? path = !string.IsNullOrWhiteSpace(configuredPath)
            ? configuredPath
            : defaultRelativePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, path));
    }
}

public sealed record LoadedPolicyText(
    string? SourcePath,
    string? Text,
    string Status);

public sealed record SessionBootstrapPolicy(
    string? SourcePath,
    string? PromptText,
    string Status)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(PromptText);
}
