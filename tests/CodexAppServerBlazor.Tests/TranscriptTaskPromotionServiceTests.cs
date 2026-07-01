using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class TranscriptTaskPromotionServiceTests
{
    [Fact]
    public void CreateTaskFromTranscript_creates_new_task_with_transcript_as_user_notes()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkflowTaskBoardViewService taskBoardViewService = new(CreateProvider());
        TranscriptTaskPromotionService service = new(taskBoardViewService);

        TaskBoardTaskViewModel created = service.CreateTaskFromTranscript(
            repository.RootPath,
            "Promote chat into task",
            """
            You 10:00
            We should build the workflow.

            Codex 10:01
            Agreed.
            """);

        TaskBoardViewModel board = taskBoardViewService.GetBoard(repository.RootPath, created.Id);

        Assert.NotNull(board.SelectedTask);
        Assert.Equal("Promote chat into task", board.SelectedTask.Name);
        Assert.Equal("Proposed", board.SelectedTask.StateCode);
        Assert.Contains("We should build the workflow.", board.SelectedTask.NotesMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Agreed.", board.SelectedTask.AgentNotesMarkdown ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTaskFromTranscript_rejects_blank_title_or_transcript()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        WorkflowTaskBoardViewService taskBoardViewService = new(CreateProvider());
        TranscriptTaskPromotionService service = new(taskBoardViewService);

        Assert.Throws<InvalidOperationException>(() =>
            service.CreateTaskFromTranscript(repository.RootPath, string.Empty, "hello"));
        Assert.Throws<InvalidOperationException>(() =>
            service.CreateTaskFromTranscript(repository.RootPath, "Task", string.Empty));
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
