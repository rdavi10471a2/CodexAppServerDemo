namespace CodexAppServerBlazor.Services.Tasks;

public interface ITaskWorkflowContextService
{
    WorkflowTurnTaskContext BuildTurnContext(string workspaceRoot);
}

public sealed record WorkflowTurnTaskContext(
    string? PromptMarkdown,
    string Status,
    string? ActiveTaskId)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(PromptMarkdown);
}
