using System.Text.Json;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class TurnUsageHistoryServiceTests
{
    [Fact]
    public void RecordTurn_persists_usage_with_declared_session_files()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime"
            })
            .Build();
        CodingServicesSettingsProvider settingsProvider = new(configuration, new TestHostEnvironment(repository.RootPath));
        TurnUsageHistoryService service = new(settingsProvider);
        CodingServicesSettings settings = settingsProvider.GetSettings(repository.RootPath);
        WorkflowEditPaths paths = new(settings);

        Directory.CreateDirectory(paths.SessionPlansRoot);
        File.WriteAllText(
            paths.GetSessionPlanPath("edit-test"),
            JsonSerializer.Serialize(new EditSessionPlan
            {
                SessionId = "edit-test",
                DeclaredRelativePaths = ["Example.cs"],
                DeclaredWatchedFilePaths = [Path.Combine(settings.WatchedProjectFolder, "Example.cs")],
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("O")
            }));

        service.RecordTurn(
            repository.RootPath,
            "thread-1",
            "turn-1",
            WorkflowTurnMode.Work,
            "task-1",
            "edit-test",
            new CodexTelemetrySummary(100, 20, 50, 30, 200000, 10, 2, "default"),
            "turn/completed",
            "completed",
            operatorDecisionRequested: true,
            notesUpdateQuestionRequested: true);

        string usageRoot = Path.Combine(paths.HistoryRoot, "turn-usage");
        string usageFile = Assert.Single(Directory.GetFiles(usageRoot, "*.json"));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(usageFile));
        JsonElement root = document.RootElement;

        Assert.Equal("thread-1", root.GetProperty("threadId").GetString());
        Assert.Equal("turn-1", root.GetProperty("turnId").GetString());
        Assert.Equal("Work", root.GetProperty("mode").GetString());
        Assert.Equal("task-1", root.GetProperty("activeTaskId").GetString());
        Assert.Equal("edit-test", root.GetProperty("currentEditSessionId").GetString());
        Assert.Equal(["Example.cs"], root.GetProperty("declaredRelativePaths").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal("turn/completed", root.GetProperty("terminalEventType").GetString());
        Assert.Equal("completed", root.GetProperty("terminalSummary").GetString());
        Assert.True(root.GetProperty("operatorDecisionRequested").GetBoolean());
        Assert.True(root.GetProperty("notesUpdateQuestionRequested").GetBoolean());
        Assert.Equal(100, root.GetProperty("inputTokens").GetInt32());
        Assert.Equal(20, root.GetProperty("cachedInputTokens").GetInt32());
        Assert.Equal(50, root.GetProperty("outputTokens").GetInt32());
        Assert.Equal(30, root.GetProperty("reasoningOutputTokens").GetInt32());
        Assert.Equal(200000, root.GetProperty("modelContextWindow").GetInt32());
        Assert.Equal(10, root.GetProperty("primaryUsedPercent").GetInt32());
        Assert.Equal(2, root.GetProperty("secondaryUsedPercent").GetInt32());
        Assert.Equal("default", root.GetProperty("planType").GetString());
    }
}
