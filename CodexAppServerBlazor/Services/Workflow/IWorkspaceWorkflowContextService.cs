namespace CodexAppServerBlazor.Services.Workflow;

public interface IWorkspaceWorkflowContextService
{
    WorkflowPromptSection BuildTurnContext(string workspaceRoot);
}
