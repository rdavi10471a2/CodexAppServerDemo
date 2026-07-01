using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class TaskWorkflowContextServiceTests
{
    [Fact]
    public void BuildTurnContext_loads_active_task_notes_files_and_events()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkflowTaskBoardViewService boardService = new(CreateProvider());
        TaskBoardViewModel initialBoard = boardService.GetBoard(repository.RootPath, null);

        Assert.NotNull(initialBoard.SelectedTask);
        boardService.UpdateNotes(repository.RootPath, initialBoard.SelectedTask.Id, "User note body.");
        boardService.AddFile(repository.RootPath, initialBoard.SelectedTask.Id, "CodexAppServerBlazor/Services/CodexConnectionService.cs", "split workflow seam", "code");
        boardService.AddComment(repository.RootPath, initialBoard.SelectedTask.Id, "Need Discuss vs Work turn composition.");

        TaskBoardViewModel refreshedBoard = boardService.GetBoard(repository.RootPath, initialBoard.SelectedTask.Id);
        Assert.NotNull(refreshedBoard.SelectedTask);
        string agentNotesPath = Path.Combine(
            Path.GetDirectoryName(refreshedBoard.SelectedTask.NotesMarkdownPath!)!,
            refreshedBoard.SelectedTask.Id + "-agent.md");
        File.WriteAllText(agentNotesPath, "# Agent Notes" + Environment.NewLine + Environment.NewLine + "Keep CodexConnectionService orchestration-only.");

        TaskWorkflowContextService service = new(CreateProvider());

        WorkflowTurnTaskContext context = service.BuildTurnContext(repository.RootPath);

        Assert.True(context.HasPrompt);
        Assert.Equal(initialBoard.SelectedTask.Id, context.ActiveTaskId);
        Assert.Contains("Active task context supplied by Coding Services:", context.PromptMarkdown, StringComparison.Ordinal);
        Assert.Contains("User note body.", context.PromptMarkdown, StringComparison.Ordinal);
        Assert.Contains("Keep CodexConnectionService orchestration-only.", context.PromptMarkdown, StringComparison.Ordinal);
        Assert.Contains("CodexAppServerBlazor/Services/CodexConnectionService.cs", context.PromptMarkdown, StringComparison.Ordinal);
        Assert.Contains("Need Discuss vs Work turn composition.", context.PromptMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTurnContext_skips_when_no_active_task_exists()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkflowTaskBoardViewService boardService = new(CreateProvider());
        TaskBoardViewModel initialBoard = boardService.GetBoard(repository.RootPath, null);

        Assert.NotNull(initialBoard.SelectedTask);
        boardService.MoveTask(repository.RootPath, initialBoard.SelectedTask.Id, "Done");

        TaskWorkflowContextService service = new(CreateProvider());

        WorkflowTurnTaskContext context = service.BuildTurnContext(repository.RootPath);

        Assert.False(context.HasPrompt);
        Assert.Null(context.ActiveTaskId);
        Assert.Contains("no Active task", context.Status, StringComparison.Ordinal);
    }

    private static CodingServicesSettingsProvider CreateProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "CodexAppServerWinForms_corrected.slnx"
            })
            .Build();

        return new CodingServicesSettingsProvider(configuration);
    }
}
