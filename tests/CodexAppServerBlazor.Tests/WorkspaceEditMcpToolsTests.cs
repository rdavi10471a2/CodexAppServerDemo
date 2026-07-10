using System.Text.Json;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkspaceEditMcpToolsTests
{
    [Fact]
    public void RefreshFile_accepts_relative_path_and_creates_edit_session()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "ToolSample.cs");
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class ToolSample
            {
            }
            """);

        EditSessionStatus status = tools.RefreshFile(relativePath);

        Assert.True(status.HasSession);
        Assert.True(status.WorkingFileExists);
        Assert.StartsWith("edit-", status.EditSessionId, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(watchedFilePath), status.WatchedFilePath);
    }

    [Fact]
    public void RefreshFile_with_explicit_session_id_reuses_shared_session_for_second_file()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string firstPath = Path.Combine("Features", "First.cs");
        string secondPath = Path.Combine("Features", "Second.cs");
        CreateWatchedFile(repository.RootPath, firstPath, "public class First { }");
        CreateWatchedFile(repository.RootPath, secondPath, "public class Second { }");

        EditSessionStatus first = tools.RefreshFile(firstPath);
        EditSessionStatus second = tools.RefreshFile(secondPath, first.EditSessionId);

        Assert.Equal(first.EditSessionId, second.EditSessionId);
    }

    [Fact]
    public void RefreshFile_without_explicit_session_id_fails_closed_when_another_session_is_active()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string firstPath = Path.Combine("Features", "First.cs");
        string secondPath = Path.Combine("Features", "Second.cs");
        CreateWatchedFile(repository.RootPath, firstPath, "public class First { }");
        CreateWatchedFile(repository.RootPath, secondPath, "public class Second { }");

        EditSessionStatus first = tools.RefreshFile(firstPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => tools.RefreshFile(secondPath));
        Assert.Contains(first.EditSessionId, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Pass that sessionId explicitly", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEditSessionState_returns_no_session_before_refresh()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "StatusSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class StatusSample
            {
            }
            """);

        EditSessionStatus status = tools.GetEditSessionState(relativePath);

        Assert.False(status.HasSession);
        Assert.Equal(string.Empty, status.EditSessionId);
        Assert.Equal("no-session", status.Classification);
    }

    [Fact]
    public void ReplaceTextInFile_updates_working_candidate_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "ReplaceTextSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class ReplaceTextSample
            {
                public string Value => "before";
            }
            """);

        EditSessionStatus initial = tools.RefreshFile(relativePath);
        ReplaceTextResult result = tools.ReplaceTextInFile(
            relativePath,
            "\"before\"",
            "\"after\"",
            expectedMatches: 1,
            expectedWorkingHash: initial.StagedHash,
            validateOverlay: false);

        Assert.True(result.Changed);
        Assert.Contains("\"after\"", File.ReadAllText(result.WorkingFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void SubmitSymbol_updates_method_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "SubmitSymbolSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class SubmitSymbolSample
            {
                public string Execute()
                {
                    return "before";
                }
            }
            """);

        string selectorJson = JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingType: "SubmitSymbolSample",
            MemberKind: "method",
            Name: "Execute"));

        RoslynEditResult result = tools.SubmitSymbol(
            relativePath,
            selectorJson,
            """
            public string Execute()
            {
                return "after";
            }
            """,
            validateOverlay: false);

        Assert.Equal("submit_symbol", result.Operation);
        Assert.Contains("\"after\"", File.ReadAllText(result.WorkingFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void GetSymbol_reads_symbol_from_relative_path_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "GetSymbolSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class GetSymbolSample
            {
                public int Count()
                {
                    return 42;
                }
            }
            """);

        string selectorJson = JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingType: "GetSymbolSample",
            MemberKind: "method",
            Name: "Count"));

        RoslynSymbolReadResult result = tools.GetSymbol(relativePath, selectorJson);

        Assert.Equal("method", result.Kind);
        Assert.Equal("Count", result.Name);
        Assert.Contains("return 42;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_add_and_remove_methods_round_trip_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "RoundTripSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class RoundTripSample
            {
            }
            """);

        tools.AddMethod(
            relativePath,
            "RoundTripSample",
            """
            public void Execute()
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = tools.AddMethod(
            relativePath,
            "RoundTripSample",
            """
            public void Execute_remove()
            {
            }
            """,
            validateOverlay: false);

        Assert.Contains("public void Execute()", File.ReadAllText(afterSecondAdd.WorkingFilePath), StringComparison.Ordinal);
        Assert.Contains("public void Execute_remove()", File.ReadAllText(afterSecondAdd.WorkingFilePath), StringComparison.Ordinal);

        RoslynEditResult afterRemove = tools.RemoveSymbol(
            relativePath,
            JsonSerializer.Serialize(new RoslynSymbolSelector(
                ContainingType: "RoundTripSample",
                MemberKind: "method",
                Name: "Execute_remove")),
            validateOverlay: false);

        string content = File.ReadAllText(afterRemove.WorkingFilePath);
        Assert.Contains("public void Execute()", content, StringComparison.Ordinal);
        Assert.DoesNotContain("public void Execute_remove()", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_add_and_remove_properties_and_usings_round_trip_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "PropertyRoundTripSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            namespace Demo;

            public class PropertyRoundTripSample
            {
            }
            """);

        tools.AddUsing(relativePath, "System.Linq", validateOverlay: false);
        tools.AddProperty(
            relativePath,
            "PropertyRoundTripSample",
            "public string Name { get; set; }",
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = tools.AddProperty(
            relativePath,
            "PropertyRoundTripSample",
            "public string Name_remove { get; set; }",
            validateOverlay: false);

        string afterAddContent = File.ReadAllText(afterSecondAdd.WorkingFilePath);
        Assert.Contains("using System.Linq;", afterAddContent, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; }", afterAddContent, StringComparison.Ordinal);
        Assert.Contains("public string Name_remove { get; set; }", afterAddContent, StringComparison.Ordinal);

        tools.RemoveUsing(relativePath, "System.Linq", validateOverlay: false);
        RoslynEditResult afterPropertyRemove = tools.RemoveSymbol(
            relativePath,
            JsonSerializer.Serialize(new RoslynSymbolSelector(
                ContainingType: "PropertyRoundTripSample",
                MemberKind: "property",
                Name: "Name_remove")),
            validateOverlay: false);

        string finalContent = File.ReadAllText(afterPropertyRemove.WorkingFilePath);
        Assert.DoesNotContain("using System.Linq;", finalContent, StringComparison.Ordinal);
        Assert.Contains("public string Name { get; set; }", finalContent, StringComparison.Ordinal);
        Assert.DoesNotContain("public string Name_remove { get; set; }", finalContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_add_and_remove_nested_types_round_trip_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "NestedTypeRoundTripSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class NestedTypeRoundTripSample
            {
            }
            """);

        tools.AddNestedType(
            relativePath,
            "NestedTypeRoundTripSample",
            """
            public class InnerType
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = tools.AddNestedType(
            relativePath,
            "NestedTypeRoundTripSample",
            """
            public class InnerType_remove
            {
            }
            """,
            validateOverlay: false);

        string afterAddContent = File.ReadAllText(afterSecondAdd.WorkingFilePath);
        Assert.Contains("public class InnerType", afterAddContent, StringComparison.Ordinal);
        Assert.Contains("public class InnerType_remove", afterAddContent, StringComparison.Ordinal);

        RoslynEditResult afterRemove = tools.RemoveSymbol(
            relativePath,
            JsonSerializer.Serialize(new RoslynSymbolSelector(
                ContainingType: "NestedTypeRoundTripSample",
                MemberKind: "class",
                Name: "InnerType_remove")),
            validateOverlay: false);

        string finalContent = File.ReadAllText(afterRemove.WorkingFilePath);
        Assert.Contains("public class InnerType", finalContent, StringComparison.Ordinal);
        Assert.DoesNotContain("public class InnerType_remove", finalContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SetTypePartial_toggles_partial_modifier_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string relativePath = Path.Combine("Features", "PartialSample.cs");
        CreateWatchedFile(
            repository.RootPath,
            relativePath,
            """
            public class PartialSample
            {
            }
            """);

        RoslynEditResult afterSetPartial = tools.SetTypePartial(
            relativePath,
            "PartialSample",
            isPartial: true,
            validateOverlay: false);

        string afterAddContent = File.ReadAllText(afterSetPartial.WorkingFilePath);
        Assert.Contains("public partial class PartialSample", afterAddContent, StringComparison.Ordinal);

        RoslynEditResult afterUnsetPartial = tools.SetTypePartial(
            relativePath,
            "PartialSample",
            isPartial: false,
            validateOverlay: false);

        string finalContent = File.ReadAllText(afterUnsetPartial.WorkingFilePath);
        Assert.Contains("public class PartialSample", finalContent, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class PartialSample", finalContent, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFileOutline_rejects_path_outside_selected_workspace()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkspaceEditMcpTools tools = CreateTools(repository.RootPath);
        string outsideFile = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.cs");

        try
        {
            File.WriteAllText(outsideFile, "public class Outside {}");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => tools.GetFileOutline(outsideFile));

            Assert.Contains("must stay within the selected workspace", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outsideFile))
            {
                File.Delete(outsideFile);
            }
        }
    }

    private static WorkspaceEditMcpTools CreateTools(string workspaceRoot)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "CodexAppServerWinForms_corrected.slnx"
            })
            .Build();

        WorkspaceState workspaceState = new();
        workspaceState.SetRepoRoot(workspaceRoot);
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        HarnessWorkspaceEditService editService = new(workspaceState, settingsProvider);
        return new WorkspaceEditMcpTools(editService);
    }

    private static string CreateWatchedFile(string repositoryRoot, string relativePath, string content)
    {
        string filePath = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        return filePath;
    }
}
