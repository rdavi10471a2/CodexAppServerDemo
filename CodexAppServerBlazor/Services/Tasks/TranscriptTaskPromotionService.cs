using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;

namespace CodexAppServerBlazor.Services.Tasks;

public sealed class TranscriptTaskPromotionService : ITranscriptTaskPromotionService
{
    private readonly IWorkflowTaskBoardViewService taskBoardViewService;

    public TranscriptTaskPromotionService(IWorkflowTaskBoardViewService taskBoardViewService)
    {
        this.taskBoardViewService = taskBoardViewService;
    }

    public TaskBoardTaskViewModel CreateTaskFromTranscript(
        string workspaceRoot,
        string taskName,
        string transcriptText)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException("Workspace root is required.");
        }

        if (string.IsNullOrWhiteSpace(taskName))
        {
            throw new InvalidOperationException("Task title is required.");
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            throw new InvalidOperationException("Transcript text is required to create a task from discussion.");
        }

        return taskBoardViewService.CreateTask(
            workspaceRoot,
            taskName.Trim(),
            shortName: null,
            notesMarkdown: transcriptText.Trim());
    }
}
