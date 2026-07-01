using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;

namespace CodexAppServerBlazor.Services.Tasks;

public interface IWorkflowTaskBoardViewService
{
    TaskBoardViewModel GetBoard(string workspaceRoot, string? selectedTaskId);

    TaskBoardTaskViewModel CreateTask(string workspaceRoot, string name, string? shortName, string? notesMarkdown);

    TaskBoardTaskViewModel UpdateTaskDetails(string workspaceRoot, string taskId, string name, string? shortName);

    TaskBoardTaskViewModel MoveTask(string workspaceRoot, string taskId, string stateCode);

    TaskBoardTaskViewModel UpdateNotes(string workspaceRoot, string taskId, string notesMarkdown);

    TaskBoardTaskViewModel ArchiveTask(string workspaceRoot, string taskId);

    TaskBoardTaskViewModel RestoreTask(string workspaceRoot, string taskId);

    void AddFile(string workspaceRoot, string taskId, string relativePath, string? intent, string? fileRole);

    void AddComment(string workspaceRoot, string taskId, string message);

    string ReadArchivedDiscussionContent(string workspaceRoot, string archivedDiscussionId);
}
