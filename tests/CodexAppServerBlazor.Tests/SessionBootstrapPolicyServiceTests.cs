using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CodexAppServerBlazor.Tests;

public sealed class SessionBootstrapPolicyServiceTests
{
    [Fact]
    public void LoadPolicy_combines_host_agents_and_session_bootstrap()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string contentRoot = Path.Combine(repository.RootPath, "CodexAppServerBlazor");
        Directory.CreateDirectory(contentRoot);

        string hostAgentsPath = Path.Combine(repository.RootPath, "AGENTS.md");
        File.WriteAllText(hostAgentsPath, "Host rule.");

        string bootstrapPath = Path.Combine(contentRoot, "CS-SessionBootstrap.txt");
        File.WriteAllText(
            bootstrapPath,
            """
            Bootstrap rule.
            - In Work mode, treat the selected workspace source tree as MCP-governed write-only.
            """);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowPolicy:SessionBootstrapPath"] = "CS-SessionBootstrap.txt"
            })
            .Build();

        SessionBootstrapPolicyService service = new(
            configuration,
            new HostingEnvironmentStub(contentRoot));

        SessionBootstrapPolicy policy = service.LoadPolicy();

        Assert.NotNull(policy.PromptText);
        Assert.Contains("Effective Coding Services host governance for this session:", policy.PromptText, StringComparison.Ordinal);
        Assert.Contains("Coding Services host AGENTS.md policy:", policy.PromptText, StringComparison.Ordinal);
        Assert.Contains("Host rule.", policy.PromptText, StringComparison.Ordinal);
        Assert.Contains("Bootstrap rule.", policy.PromptText, StringComparison.Ordinal);
        Assert.Contains("MCP-governed write-only", policy.PromptText, StringComparison.Ordinal);
        Assert.Contains(hostAgentsPath, policy.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bootstrapPath, policy.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadPolicy_uses_host_agents_when_workspace_has_no_local_agents()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string contentRoot = Path.Combine(repository.RootPath, "CodexAppServerBlazor");
        Directory.CreateDirectory(contentRoot);

        string hostAgentsPath = Path.Combine(repository.RootPath, "AGENTS.md");
        File.WriteAllText(hostAgentsPath, "Host governance.");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        SessionBootstrapPolicyService service = new(
            configuration,
            new HostingEnvironmentStub(contentRoot));

        SessionBootstrapPolicy policy = service.LoadPolicy();

        Assert.NotNull(policy.PromptText);
        Assert.Contains("active session policy", policy.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected workspace has no local AGENTS.md file", policy.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Host governance.", policy.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace-local AGENTS.md policy:", policy.PromptText, StringComparison.Ordinal);
    }

    private sealed class HostingEnvironmentStub : IHostEnvironment
    {
        public HostingEnvironmentStub(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "CodexAppServerBlazor.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = default!;
    }
}
