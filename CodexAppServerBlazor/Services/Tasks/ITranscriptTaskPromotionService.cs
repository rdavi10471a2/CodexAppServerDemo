using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;

namespace CodexAppServerBlazor.Services.Tasks;

public interface ITranscriptTaskPromotionService
{
    TaskBoardTaskViewModel CreateTaskFromTranscript(
        string workspaceRoot,
        string taskName,
        string transcriptText);
}
