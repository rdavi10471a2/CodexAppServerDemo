using CodexAppServerBlazor.Services.Tasks;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed record WorkflowTurnEnvelope(
    string Prompt,
    WorkflowTurnMode Mode,
    bool IncludedSessionBootstrap,
    bool IncludedWorkspaceContext,
    SessionBootstrapPolicy SessionBootstrapPolicy,
    WorkflowPromptSection WorkspaceContext,
    WorkflowTurnTaskContext TaskContext);
