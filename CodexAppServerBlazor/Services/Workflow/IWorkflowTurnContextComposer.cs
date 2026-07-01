using CodexAppServerBlazor.Services.Tasks;

namespace CodexAppServerBlazor.Services.Workflow;

public interface IWorkflowTurnContextComposer
{
    WorkflowTurnEnvelope Compose(
        string userPrompt,
        string workspaceRoot,
        WorkflowTurnMode mode,
        WorkflowSessionState sessionState,
        WorkflowPromptSection workspaceContext,
        WorkflowTurnTaskContext taskContext);
}
