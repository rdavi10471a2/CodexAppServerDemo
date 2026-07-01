using System.Text;
using CodexAppServerBlazor.Services.Tasks;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkflowTurnContextComposer : IWorkflowTurnContextComposer
{
    public WorkflowTurnEnvelope Compose(
        string userPrompt,
        string workspaceRoot,
        WorkflowTurnMode mode,
        WorkflowSessionState sessionState,
        WorkflowPromptSection workspaceContext,
        WorkflowTurnTaskContext taskContext)
    {
        if (mode == WorkflowTurnMode.Work && !taskContext.HasPrompt)
        {
            throw new InvalidOperationException("Cannot start a Work turn without active task context. " + taskContext.Status);
        }

        bool includeWorkspaceContext = !sessionState.HasAttachedWorkspaceContext && workspaceContext.HasPrompt;

        StringBuilder prompt = new();
        prompt.AppendLine(userPrompt);
        prompt.AppendLine();

        if (mode == WorkflowTurnMode.Work)
        {
            prompt.AppendLine(taskContext.PromptMarkdown);
            prompt.AppendLine();
        }

        if (includeWorkspaceContext)
        {
            prompt.AppendLine(workspaceContext.PromptMarkdown);
            prompt.AppendLine();
        }

        prompt.AppendLine("Codex cwd:");
        prompt.AppendLine(workspaceRoot);
        prompt.AppendLine();
        prompt.AppendLine("Workflow mode:");
        prompt.AppendLine("- " + mode);
        prompt.AppendLine();
        prompt.AppendLine("Workspace boundary:");
        prompt.AppendLine("- Treat the cwd above as the loaded workspace.");
        prompt.AppendLine("- Do not use selected-file assumptions for this turn.");
        if (mode == WorkflowTurnMode.Work)
        {
            prompt.AppendLine("- Use discovery, proposal, edit/diff, compile, and reindex order when work is requested.");
            prompt.AppendLine("- Assume indexed MCP results are stale after any code edit. Re-run get_watched_solution_digest before using prior indexed structure, and re-run get_watched_solution_summary or get_test_project_summary after compile/reindex when relevant.");
            prompt.AppendLine("- Keep durable workflow memory in task notes/files/events; treat the solution index as a volatile lookup surface.");
        }
        else
        {
            prompt.AppendLine("- Treat this as discussion/planning/review by default unless the user explicitly asks for code changes.");
            prompt.AppendLine("- Do not assume durable task context is loaded for this turn.");
            prompt.AppendLine("- If code structure matters, refresh digest or MCP summaries rather than relying on stale transcript context.");
        }

        prompt.AppendLine("- When the user asks for tool results, report the results in the same response after the tool call completes; do not wait for a follow-up prompt.");

        return new WorkflowTurnEnvelope(
            prompt.ToString(),
            mode,
            includeWorkspaceContext,
            workspaceContext,
            taskContext);
    }
}
