using CodexAppServerBlazor.Services.Tasks;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed record WorkflowTurnEnvelope(
    string Prompt,
    WorkflowTurnMode Mode,
    bool IncludedWorkspaceContext,
    WorkflowPromptSection WorkspaceContext,
    WorkflowTurnTaskContext TaskContext);
