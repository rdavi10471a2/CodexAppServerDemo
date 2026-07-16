using System.Text.Json;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using Xunit.Abstractions;

namespace CodexAppServerBlazor.Tests;

public sealed class RoslynEditServiceTests
{
    private readonly ITestOutputHelper output;

    public RoslynEditServiceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void GetFileOutline_returns_type_and_method_items_for_csharp_file()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/OutlineSample.cs",
            """
            namespace Demo;

            public class OutlineSample
            {
                public void Run()
                {
                }
            }
            """);

        RoslynFileOutlineResult result = service.GetFileOutline(watchedFilePath);

        Assert.Equal("parsed", result.ParseStatus);
        Assert.Contains(result.Items, item => item.Kind == "class" && item.Name == "OutlineSample");
        Assert.Contains(result.Items, item => item.Kind == "method" && item.Name == "Run");
    }

    [Fact]
    public void GetSourceMap_reports_dependency_injection_registrations_for_registered_symbols()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        CreateWatchedFile(
            repository.RootPath,
            "Program.cs",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            var services = new ServiceCollection();
            services.AddScoped<IGreeter, Greeter>();
            services.AddKeyedSingleton<IClock, SystemClock>("utc");
            services.TryAdd(ServiceDescriptor.Transient(typeof(IFormatter), typeof(DefaultFormatter)));
            """);
        CreateWatchedFile(
            repository.RootPath,
            "Features/DiServices.cs",
            """
            public interface IGreeter {}
            public sealed class Greeter : IGreeter {}

            public interface IClock {}
            public sealed class SystemClock : IClock {}

            public interface IFormatter {}
            public sealed class DefaultFormatter : IFormatter {}
            """);

        RoslynSourceMapResult result = service.GetSourceMap(path: null, mode: "detail");

        RoslynSourceMapSymbol greeterInterface = FindSourceMapSymbol(result, "interface", "IGreeter");
        RoslynSourceMapSymbol greeterImplementation = FindSourceMapSymbol(result, "class", "Greeter");
        RoslynSourceMapSymbol clockImplementation = FindSourceMapSymbol(result, "class", "SystemClock");
        RoslynSourceMapSymbol formatterInterface = FindSourceMapSymbol(result, "interface", "IFormatter");

        Assert.True(greeterInterface.IsDiRegistered);
        Assert.Contains(greeterInterface.DiRegistrations!, registration =>
            registration.MatchRole == "service"
            && registration.Lifetime == "scoped"
            && registration.RegistrationMethod == "AddScoped"
            && registration.ServiceType == "IGreeter"
            && registration.ImplementationType == "Greeter");

        Assert.True(greeterImplementation.IsDiRegistered);
        Assert.Contains(greeterImplementation.DiRegistrations!, registration =>
            registration.MatchRole == "implementation"
            && registration.ServiceType == "IGreeter"
            && registration.ImplementationType == "Greeter");

        Assert.True(clockImplementation.IsDiRegistered);
        Assert.Contains(clockImplementation.DiRegistrations!, registration =>
            registration.Lifetime == "singleton"
            && registration.IsKeyed == true
            && registration.RegistrationMethod == "AddKeyedSingleton");

        Assert.True(formatterInterface.IsDiRegistered);
        Assert.Contains(formatterInterface.DiRegistrations!, registration =>
            registration.Lifetime == "transient"
            && registration.RegistrationMethod == "TryAdd:Transient"
            && registration.ServiceType == "IFormatter"
            && registration.ImplementationType == "DefaultFormatter");
    }

    [Fact]
    public void SubmitSymbol_replaces_existing_method_body()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/Greeter.cs",
            """
            public class Greeter
            {
                public string SayHello()
                {
                    return "Hello";
                }
            }
            """);

        string selectorJson = SerializeSelector("Greeter", "method", "SayHello");

        RoslynEditResult result = service.SubmitSymbol(
            watchedFilePath,
            selectorJson,
            """
            public string SayHello()
            {
                return "Hi";
            }
            """,
            validateOverlay: false);

        string updated = File.ReadAllText(result.WorkingFilePath);
        Assert.Equal("submit_symbol", result.Operation);
        Assert.Contains("return \"Hi\";", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("return \"Hello\";", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Method_round_trip_removes_only_the_remove_variant()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/Worker.cs",
            """
            public class Worker
            {
            }
            """);

        service.AddMethod(
            watchedFilePath,
            "Worker",
            """
            public void Execute()
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = service.AddMethod(
            watchedFilePath,
            "Worker",
            """
            public void Execute_remove()
            {
            }
            """,
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondAdd.WorkingFilePath,
            "public void Execute()",
            "public void Execute_remove()");

        RoslynEditResult afterRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("Worker", "method", "Execute_remove"),
            validateOverlay: false);

        AssertFileContains(afterRemove.WorkingFilePath, "public void Execute()");
        AssertFileDoesNotContain(afterRemove.WorkingFilePath, "public void Execute_remove()");
    }

    [Fact]
    public void Property_round_trip_removes_only_the_remove_variant()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/PropertySample.cs",
            """
            public class PropertySample
            {
            }
            """);

        service.AddProperty(
            watchedFilePath,
            "PropertySample",
            "public string Name { get; set; }",
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = service.AddProperty(
            watchedFilePath,
            "PropertySample",
            "public string Name_remove { get; set; }",
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondAdd.WorkingFilePath,
            "public string Name { get; set; }",
            "public string Name_remove { get; set; }");

        RoslynEditResult afterRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("PropertySample", "property", "Name_remove"),
            validateOverlay: false);

        AssertFileContains(afterRemove.WorkingFilePath, "public string Name { get; set; }");
        AssertFileDoesNotContain(afterRemove.WorkingFilePath, "public string Name_remove { get; set; }");
    }

    [Fact]
    public void Field_round_trip_removes_only_the_remove_variant()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/FieldSample.cs",
            """
            public class FieldSample
            {
            }
            """);

        service.AddField(
            watchedFilePath,
            "FieldSample",
            "private int count;",
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = service.AddField(
            watchedFilePath,
            "FieldSample",
            "private int count_remove;",
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondAdd.WorkingFilePath,
            "private int count;",
            "private int count_remove;");

        RoslynEditResult afterRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("FieldSample", "field", "count_remove"),
            validateOverlay: false);

        AssertFileContains(afterRemove.WorkingFilePath, "private int count;");
        AssertFileDoesNotContain(afterRemove.WorkingFilePath, "private int count_remove;");
    }

    [Fact]
    public void Constructor_round_trip_removes_only_the_remove_variant()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/ConstructorSample.cs",
            """
            public class ConstructorSample
            {
            }
            """);

        service.AddConstructor(
            watchedFilePath,
            "ConstructorSample",
            """
            public ConstructorSample()
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = service.AddConstructor(
            watchedFilePath,
            "ConstructorSample",
            """
            public ConstructorSample(string name_remove)
            {
            }
            """,
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondAdd.WorkingFilePath,
            "public ConstructorSample()",
            "public ConstructorSample(string name_remove)");

        RoslynEditResult afterRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("ConstructorSample", "constructor", "ConstructorSample", ["string"]),
            validateOverlay: false);

        AssertFileContains(afterRemove.WorkingFilePath, "public ConstructorSample()");
        AssertFileDoesNotContain(afterRemove.WorkingFilePath, "public ConstructorSample(string name_remove)");
    }

    [Fact]
    public void NestedType_round_trip_removes_only_the_remove_variant()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/NestedTypeSample.cs",
            """
            public class NestedTypeSample
            {
            }
            """);

        service.AddNestedType(
            watchedFilePath,
            "NestedTypeSample",
            """
            public class InnerType
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondAdd = service.AddNestedType(
            watchedFilePath,
            "NestedTypeSample",
            """
            public class InnerType_remove
            {
            }
            """,
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondAdd.WorkingFilePath,
            "public class InnerType",
            "public class InnerType_remove");

        RoslynEditResult afterRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("NestedTypeSample", "class", "InnerType_remove"),
            validateOverlay: false);

        AssertFileContains(afterRemove.WorkingFilePath, "public class InnerType");
        AssertFileDoesNotContain(afterRemove.WorkingFilePath, "public class InnerType_remove");
    }

    [Fact]
    public void AddUsing_and_RemoveUsing_update_working_candidate()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/UsingSample.cs",
            """
            namespace Demo;

            public class UsingSample
            {
            }
            """);

        RoslynEditResult added = service.AddUsing(
            watchedFilePath,
            "System.Linq",
            validateOverlay: false);
        AssertFileContains(added.WorkingFilePath, "using System.Linq;");

        RoslynEditResult removed = service.RemoveUsing(
            watchedFilePath,
            "System.Linq",
            validateOverlay: false);
        AssertFileDoesNotContain(removed.WorkingFilePath, "using System.Linq;");
    }

    [Fact]
    public void Attributed_class_and_method_round_trip_remove_only_the_remove_variants()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/AttributedSample.cs",
            """
            using System;

            public class AttributedSample
            {
            }
            """);

        service.AddNestedType(
            watchedFilePath,
            "AttributedSample",
            """
            [Serializable]
            public class InnerAttributed
            {
            }
            """,
            validateOverlay: false);

        service.AddMethod(
            watchedFilePath,
            "AttributedSample",
            """
            [Obsolete]
            public void RunAttributed()
            {
            }
            """,
            validateOverlay: false);

        service.AddNestedType(
            watchedFilePath,
            "AttributedSample",
            """
            [Serializable]
            public class InnerAttributed_remove
            {
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterSecondMethodAdd = service.AddMethod(
            watchedFilePath,
            "AttributedSample",
            """
            [Obsolete]
            public void RunAttributed_remove()
            {
            }
            """,
            validateOverlay: false);

        AssertFileContainsAll(
            afterSecondMethodAdd.WorkingFilePath,
            "[Serializable]",
            "public class InnerAttributed",
            "public class InnerAttributed_remove",
            "[Obsolete]",
            "public void RunAttributed()",
            "public void RunAttributed_remove()");

        service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("AttributedSample", "class", "InnerAttributed_remove"),
            validateOverlay: false);

        RoslynEditResult afterMethodRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("AttributedSample", "method", "RunAttributed_remove"),
            validateOverlay: false);

        AssertFileContains(afterMethodRemove.WorkingFilePath, "public class InnerAttributed");
        AssertFileDoesNotContain(afterMethodRemove.WorkingFilePath, "public class InnerAttributed_remove");
        AssertFileContains(afterMethodRemove.WorkingFilePath, "public void RunAttributed()");
        AssertFileDoesNotContain(afterMethodRemove.WorkingFilePath, "public void RunAttributed_remove()");
        AssertFileContains(afterMethodRemove.WorkingFilePath, "[Serializable]");
        AssertFileContains(afterMethodRemove.WorkingFilePath, "[Obsolete]");
    }

    [Fact]
    public void Fake_mcp_tool_members_round_trip_remove_only_the_remove_variants()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/FakeMcpTools.cs",
            """
            using System.ComponentModel;
            using ModelContextProtocol.Server;

            [McpServerToolType]
            public sealed class FakeMcpTools
            {
                [McpServerTool]
                [Description("Returns the current workspace metadata.")]
                public string GetWorkspace()
                {
                    return "ok";
                }

                [Description("Displays the current fake MCP state.")]
                public string StateLabel => "ready";
            }
            """);

        RoslynEditResult afterMethodAdd = service.AddMethod(
            watchedFilePath,
            "FakeMcpTools",
            """
            [McpServerTool]
            [Description("Returns the current workspace metadata remove variant.")]
            public string GetWorkspace_remove()
            {
                return "remove";
            }
            """,
            validateOverlay: false);

        RoslynEditResult afterPropertyAdd = service.AddProperty(
            watchedFilePath,
            "FakeMcpTools",
            """
            [Description("Displays the current fake MCP state remove variant.")]
            public string StateLabel_remove => "remove";
            """,
            validateOverlay: false);

        AssertFileContainsAll(
            afterPropertyAdd.WorkingFilePath,
            "[McpServerToolType]",
            "[McpServerTool]",
            "public string GetWorkspace()",
            "public string GetWorkspace_remove()",
            "public string StateLabel => \"ready\";",
            "public string StateLabel_remove => \"remove\";",
            "[Description(\"Returns the current workspace metadata.\")]",
            "[Description(\"Returns the current workspace metadata remove variant.\")]",
            "[Description(\"Displays the current fake MCP state.\")]",
            "[Description(\"Displays the current fake MCP state remove variant.\")]");

        service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("FakeMcpTools", "method", "GetWorkspace_remove"),
            validateOverlay: false);

        RoslynEditResult afterPropertyRemove = service.RemoveSymbol(
            watchedFilePath,
            SerializeSelector("FakeMcpTools", "property", "StateLabel_remove"),
            validateOverlay: false);

        AssertFileContains(afterPropertyRemove.WorkingFilePath, "public string GetWorkspace()");
        AssertFileDoesNotContain(afterPropertyRemove.WorkingFilePath, "public string GetWorkspace_remove()");
        AssertFileContains(afterPropertyRemove.WorkingFilePath, "public string StateLabel => \"ready\";");
        AssertFileDoesNotContain(afterPropertyRemove.WorkingFilePath, "public string StateLabel_remove => \"remove\";");
        AssertFileContains(afterPropertyRemove.WorkingFilePath, "[McpServerToolType]");
        AssertFileContains(afterPropertyRemove.WorkingFilePath, "[McpServerTool]");
        AssertFileContains(afterPropertyRemove.WorkingFilePath, "[Description(\"Returns the current workspace metadata.\")]");
        AssertFileContains(afterPropertyRemove.WorkingFilePath, "[Description(\"Displays the current fake MCP state.\")]");
    }

    [Fact]
    public void Add_members_and_submit_symbol_style_rewrite_can_produce_different_file_shapes_for_same_semantic_change()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        const string originalText =
            """
            namespace Demo;

            public class Unused11
            {
                public int Value { get; set; }

                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }
            }
            """;

        string addMembersPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.AddMembers.cs", originalText);
        string submitSymbolPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.SubmitSymbol.cs", originalText);

        string beforeAddMembers = File.ReadAllText(addMembersPath);
        RoslynEditResult afterPropertyAdd = service.AddProperty(
            addMembersPath,
            "Unused11",
            "public string Label { get; set; } = string.Empty;",
            afterSymbol: "Value",
            validateOverlay: false);
        string afterPropertyText = File.ReadAllText(afterPropertyAdd.WorkingFilePath);

        RoslynEditResult afterMethodAdd = service.AddMethod(
            addMembersPath,
            "Unused11",
            """
            public string Describe()
            {
                return $"{Label}:{Value}";
            }
            """,
            afterSymbol: "AddOne",
            validateOverlay: false);
        string addMembersFinalText = File.ReadAllText(afterMethodAdd.WorkingFilePath);

        string classSelectorJson = JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingNamespace: "Demo",
            MemberKind: "class",
            Name: "Unused11"));
        RoslynEditResult submitSymbolResult = service.SubmitSymbol(
            submitSymbolPath,
            classSelectorJson,
            """
            public class Unused11
            {
                public int Value { get; set; }

                public string Label { get; set; } = string.Empty;

                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }

                public string Describe()
                {
                    return $"{Label}:{Value}";
                }
            }
            """,
            validateOverlay: false);
        string submitSymbolFinalText = File.ReadAllText(submitSymbolResult.WorkingFilePath);

        output.WriteLine("Before add-members path:");
        output.WriteLine(beforeAddMembers);
        output.WriteLine("After add_property:");
        output.WriteLine(afterPropertyText);
        output.WriteLine("After add_method:");
        output.WriteLine(addMembersFinalText);
        output.WriteLine("After submit_symbol class rewrite:");
        output.WriteLine(submitSymbolFinalText);
        output.WriteLine("Final texts equal: " + string.Equals(addMembersFinalText, submitSymbolFinalText, StringComparison.Ordinal));

        Assert.Contains("public string Label { get; set; } = string.Empty;", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("return $\"{Label}:{Value}\";", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Label { get; set; } = string.Empty;", submitSymbolFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", submitSymbolFinalText, StringComparison.Ordinal);
        Assert.Contains("return $\"{Label}:{Value}\";", submitSymbolFinalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_code_churn_remove_and_restore_can_be_compared_between_add_members_and_submit_symbol()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        const string seededText =
            """
            namespace Demo;

            public class Unused11
            {
                public int Value { get; set; }

                public string Label { get; set; } = string.Empty;

                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }

                public string Describe()
                {
                    return $"{Label}:{Value}";
                }
            }
            """;

        string addMembersPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.ChurnAddMembers.cs", seededText);
        string submitSymbolPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.ChurnSubmitSymbol.cs", seededText);

        string beforeChurn = File.ReadAllText(addMembersPath);

        RoslynEditResult variantPropertyAdd = service.AddProperty(
            addMembersPath,
            "Unused11",
            "public string Badge { get; set; } = \"seed\";",
            afterSymbol: "Label",
            validateOverlay: false);
        RoslynEditResult variantMethodAdd = service.AddMethod(
            addMembersPath,
            "Unused11",
            """
            public string DescribeVerbose()
            {
                return $"{Label}:{Badge}:{Value}";
            }
            """,
            afterSymbol: "Describe",
            validateOverlay: false);

        string afterVariantAdds = File.ReadAllText(variantMethodAdd.WorkingFilePath);

        service.RemoveSymbol(
            addMembersPath,
            SerializeSelector("Unused11", "property", "Label"),
            validateOverlay: false);
        RoslynEditResult afterDescribeRemove = service.RemoveSymbol(
            addMembersPath,
            SerializeSelector("Unused11", "method", "Describe"),
            validateOverlay: false);

        string churnedWithoutOriginals = File.ReadAllText(afterDescribeRemove.WorkingFilePath);

        File.WriteAllText(submitSymbolPath, churnedWithoutOriginals);

        RoslynEditResult addMembersPropertyRestore = service.AddProperty(
            addMembersPath,
            "Unused11",
            "public string Label { get; set; } = string.Empty;",
            afterSymbol: "Value",
            validateOverlay: false);
        RoslynEditResult addMembersMethodRestore = service.AddMethod(
            addMembersPath,
            "Unused11",
            """
            public string Describe()
            {
                return $"{Label}:{Value}";
            }
            """,
            afterSymbol: "AddOne",
            validateOverlay: false);
        string addMembersFinalText = File.ReadAllText(addMembersMethodRestore.WorkingFilePath);

        string classSelectorJson = JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingNamespace: "Demo",
            MemberKind: "class",
            Name: "Unused11"));
        RoslynEditResult submitSymbolRestore = service.SubmitSymbol(
            submitSymbolPath,
            classSelectorJson,
            """
            public class Unused11
            {
                public int Value { get; set; }

                public string Label { get; set; } = string.Empty;

                public string Badge { get; set; } = "seed";

                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }

                public string Describe()
                {
                    return $"{Label}:{Value}";
                }

                public string DescribeVerbose()
                {
                    return $"{Label}:{Badge}:{Value}";
                }
            }
            """,
            validateOverlay: false);
        string submitSymbolFinalText = File.ReadAllText(submitSymbolRestore.WorkingFilePath);

        output.WriteLine("Before churn:");
        output.WriteLine(beforeChurn);
        output.WriteLine("After adding variant symbols:");
        output.WriteLine(afterVariantAdds);
        output.WriteLine("Churned intermediate with originals removed:");
        output.WriteLine(churnedWithoutOriginals);
        output.WriteLine("After add-members restore:");
        output.WriteLine(addMembersFinalText);
        output.WriteLine("After submit_symbol restore:");
        output.WriteLine(submitSymbolFinalText);
        output.WriteLine($"Final texts equal after churn: {string.Equals(addMembersFinalText, submitSymbolFinalText, StringComparison.Ordinal)}");

        Assert.Contains("public string Label { get; set; } = string.Empty;", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Badge { get; set; } = \"seed\";", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", addMembersFinalText, StringComparison.Ordinal);
        Assert.Contains("public string DescribeVerbose()", addMembersFinalText, StringComparison.Ordinal);

        Assert.Contains("public string Label { get; set; } = string.Empty;", submitSymbolFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Badge { get; set; } = \"seed\";", submitSymbolFinalText, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", submitSymbolFinalText, StringComparison.Ordinal);
        Assert.Contains("public string DescribeVerbose()", submitSymbolFinalText, StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_unused11_fixture_can_reveal_member_insertion_position_differences()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        const string originalText =
            """
            namespace SchemaStudioWebViewer;

            public class Unused11
            {
                public int Value { get; private set; }



                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }

            }
            """;

        string addMembersPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.ExactFixture.AddMembers.cs", originalText);
        string submitSymbolPath = CreateWatchedFile(repository.RootPath, "Features/Unused11.ExactFixture.SubmitSymbol.cs", originalText);

        RoslynEditResult propertyAdd = service.AddProperty(
            addMembersPath,
            "Unused11",
            "public string Label { get; set; } = string.Empty;",
            afterSymbol: "Value",
            validateOverlay: false);
        string afterPropertyAdd = File.ReadAllText(propertyAdd.WorkingFilePath);

        RoslynEditResult methodAdd = service.AddMethod(
            addMembersPath,
            "Unused11",
            """
            public string Describe()
            {
                return $"{Label}:{Value}";
            }
            """,
            afterSymbol: "AddOne",
            validateOverlay: false);
        string afterMethodAdd = File.ReadAllText(methodAdd.WorkingFilePath);

        string classSelectorJson = JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingNamespace: "SchemaStudioWebViewer",
            MemberKind: "class",
            Name: "Unused11"));
        RoslynEditResult submitSymbolResult = service.SubmitSymbol(
            submitSymbolPath,
            classSelectorJson,
            """
            public class Unused11
            {
                public int Value { get; private set; }

                public string Label { get; set; } = string.Empty;

                public Unused11(int value)
                {
                    Value = value;
                }

                public int AddOne()
                {
                    Value++;
                    return Value;
                }

                public string Describe()
                {
                    return $"{Label}:{Value}";
                }
            }
            """,
            validateOverlay: false);
        string afterSubmitSymbol = File.ReadAllText(submitSymbolResult.WorkingFilePath);

        output.WriteLine("Exact fixture original:");
        output.WriteLine(originalText.Replace("\r\n", "\n", StringComparison.Ordinal));
        output.WriteLine("Exact fixture after add_property:");
        output.WriteLine(afterPropertyAdd);
        output.WriteLine("Exact fixture after add_method:");
        output.WriteLine(afterMethodAdd);
        output.WriteLine("Exact fixture after submit_symbol:");
        output.WriteLine(afterSubmitSymbol);
        output.WriteLine($"Exact fixture final texts equal: {string.Equals(afterMethodAdd, afterSubmitSymbol, StringComparison.Ordinal)}");

        Assert.Contains("public string Label { get; set; } = string.Empty;", afterMethodAdd, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", afterMethodAdd, StringComparison.Ordinal);
        Assert.Contains("public string Label { get; set; } = string.Empty;", afterSubmitSymbol, StringComparison.Ordinal);
        Assert.Contains("public string Describe()", afterSubmitSymbol, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFileOutline_rejects_razor_markup_path_with_guidance()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        RoslynEditService service = new(CreateSettings(repository.RootPath));
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Components/Sample.razor",
            "<h1>Hello</h1>");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.GetFileOutline(watchedFilePath));

        Assert.Contains("cannot read or edit Razor markup directly", ex.Message, StringComparison.Ordinal);
        Assert.Contains("replace_text_in_file", ex.Message, StringComparison.Ordinal);
    }

    private static CodingServicesSettings CreateSettings(string repositoryRoot)
    {
        string solutionPath = Path.Combine(repositoryRoot, "CodexAppServerWinForms_corrected.slnx");
        return CodingServicesSettings.Create(
            repositoryRoot,
            solutionPath,
            runtimeRoot: Path.Combine(repositoryRoot, "runtime-test"));
    }

    private static string CreateWatchedFile(string repositoryRoot, string relativePath, string content)
    {
        string filePath = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        return filePath;
    }

    private static string SerializeSelector(
        string containingType,
        string memberKind,
        string name,
        IReadOnlyList<string>? parameterTypes = null)
    {
        return JsonSerializer.Serialize(new RoslynSymbolSelector(
            ContainingType: containingType,
            MemberKind: memberKind,
            Name: name,
            ParameterTypes: parameterTypes));
    }

    private static RoslynSourceMapSymbol FindSourceMapSymbol(RoslynSourceMapResult result, string kind, string name)
    {
        return result.Files
            .SelectMany(file => file.Symbols)
            .Single(symbol => symbol.Kind == kind && symbol.Name == name);
    }

    private static void AssertFileContains(string path, string expected)
    {
        Assert.Contains(expected, File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static void AssertFileDoesNotContain(string path, string unexpected)
    {
        Assert.DoesNotContain(unexpected, File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static void AssertFileContainsAll(string path, params string[] values)
    {
        string content = File.ReadAllText(path);
        foreach (string value in values)
        {
            Assert.Contains(value, content, StringComparison.Ordinal);
        }
    }
}
