using System.Xml.Linq;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkflowPolicyTextRegressionTests
{
    [Fact]
    public void Session_bootstrap_policy_contains_new_file_sequence_and_fail_closed_rules()
    {
        string repoRoot = FindRepoRoot();
        string policyPath = Path.Combine(repoRoot, "CodexAppServerBlazor", "docs", "policy", "CS-SessionBootstrap.txt");

        string text = File.ReadAllText(policyPath);

        Assert.Contains("The governed Coding Services tools for this session live on the harness MCP surface exposed by the host.", text, StringComparison.Ordinal);
        Assert.Contains("Do not claim that the harness edit surface is unavailable, read-only, or missing until that live discovery pass is complete.", text, StringComparison.Ordinal);
        Assert.Contains("`tool_search` is a search aid, not a complete inventory primitive.", text, StringComparison.Ordinal);
        Assert.Contains("outside the current governed task", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Discovery restarts fresh each Work turn", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A governed edit session is required for every watched-source change", text, StringComparison.Ordinal);
        Assert.Contains("If `declare_session_files` is exposed and healthy", text, StringComparison.Ordinal);
        Assert.Contains("perform one broad live tool-surface discovery pass", text, StringComparison.Ordinal);
        Assert.Contains("Do not treat a narrow or relevance-filtered `tool_search` result as a complete inventory", text, StringComparison.Ordinal);
        Assert.Contains("RPC against structured local code artifacts", text, StringComparison.Ordinal);
        Assert.Contains("Roslyn-backed semantic edit tools are the required default for reliable governed editing", text, StringComparison.Ordinal);
        Assert.Contains("Use `replace_text_in_file` for trivial single contiguous literal changes.", text, StringComparison.Ordinal);
        Assert.Contains("destructive coordinate fallback", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If it is absent, continue by establishing the required governed edit session through the normal `refresh_file` / `new_file` path instead", text, StringComparison.Ordinal);
        Assert.Contains("truthful governed refusal", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Edit_tool_guidance_contains_ranked_decision_rules()
    {
        string repoRoot = FindRepoRoot();
        string guidancePath = Path.Combine(repoRoot, "CodexAppServerBlazor", "docs", "policy", "CS-EditToolGuidance.txt");

        string text = File.ReadAllText(guidancePath);

        Assert.Contains("1. Roslyn/symbol-aware mutation such as `submit_symbol`, `add_*`, or `remove_symbol`", text, StringComparison.Ordinal);
        Assert.Contains("2. `replace_text_in_file`", text, StringComparison.Ordinal);
        Assert.Contains("3. `replace_span_in_file` only when the safer governed paths cannot express the change cleanly", text, StringComparison.Ordinal);
        Assert.Contains("For C# specifically, prefer `replace_text_in_file` for one contiguous unique replacement.", text, StringComparison.Ordinal);
        Assert.Contains("prefer symbol-aware mutation such as `submit_symbol` or `remove_symbol` when the intended change spans more than one fragment", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Roslyn-backed semantic edit tools are the required default for reliable governed editing", text, StringComparison.Ordinal);
        Assert.Contains("Treat governed editing as RPC against typed local artifacts", text, StringComparison.Ordinal);
        Assert.Contains("prefer `add_using` / `remove_using` for using directives", text, StringComparison.Ordinal);
        Assert.Contains("Treat `replace_span_in_file` as a destructive coordinate fallback.", text, StringComparison.Ordinal);
        Assert.Contains("`new_file(path, sessionId)` then `submit_file(path, content)`", text, StringComparison.Ordinal);
        Assert.Contains("`submit_file` does not take `sessionId`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("submit_file(path, content, sessionId)", text, StringComparison.Ordinal);
        Assert.Contains("say so plainly and stop", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Worked_examples_file_contains_symbol_text_razor_and_new_file_flows()
    {
        string repoRoot = FindRepoRoot();
        string examplesPath = Path.Combine(repoRoot, "CodexAppServerBlazor", "docs", "policy", "CS-WorkedExamples.txt");

        string text = File.ReadAllText(examplesPath);

        Assert.Contains("Existing C# file, single contiguous replacement", text, StringComparison.Ordinal);
        Assert.Contains("Existing C# file, multi-fragment edit inside one method", text, StringComparison.Ordinal);
        Assert.Contains("Razor or mixed-markup file", text, StringComparison.Ordinal);
        Assert.Contains("Brand-new watched file", text, StringComparison.Ordinal);
        Assert.Contains("submit_symbol", text, StringComparison.Ordinal);
        Assert.Contains("replace_text_in_file", text, StringComparison.Ordinal);
        Assert.Contains("replace_span_in_file", text, StringComparison.Ordinal);
        Assert.Contains("new_file", text, StringComparison.Ordinal);
        Assert.Contains("submit_file", text, StringComparison.Ordinal);
        Assert.Contains("use `add_file_to_session(sessionId, watchedFilePath)` for normal growth", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3. If available and the full non-empty file set is already known, `declare_session_files(sessionId, [...])`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Blazor_project_includes_edit_tool_guidance_as_content()
    {
        string repoRoot = FindRepoRoot();
        string projectPath = Path.Combine(repoRoot, "CodexAppServerBlazor", "CodexAppServerBlazor.csproj");

        XDocument project = XDocument.Load(projectPath);

        bool hasGuidanceContent = project
            .Descendants("Content")
            .Any(element => string.Equals(
                (string?)element.Attribute("Include"),
                "docs\\policy\\CS-EditToolGuidance.txt",
                StringComparison.OrdinalIgnoreCase));

        Assert.True(hasGuidanceContent, "CS-EditToolGuidance.txt should remain packaged with the Blazor app.");

        bool hasExamplesContent = project
            .Descendants("Content")
            .Any(element => string.Equals(
                (string?)element.Attribute("Include"),
                "docs\\policy\\CS-WorkedExamples.txt",
                StringComparison.OrdinalIgnoreCase));

        Assert.True(hasExamplesContent, "CS-WorkedExamples.txt should remain packaged with the Blazor app.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "CodexAppServerBlazor", "docs", "policy", "CS-SessionBootstrap.txt");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the CodexAppServerWinForms_corrected repository root from the test output directory.");
    }
}
