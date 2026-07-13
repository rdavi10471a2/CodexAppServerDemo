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
        SessionBootstrapPolicy sessionBootstrapPolicy,
        WorkflowPromptSection workspaceContext,
        WorkflowTurnTaskContext taskContext)
    {
        if (mode == WorkflowTurnMode.Work && !taskContext.HasPrompt)
        {
            throw new InvalidOperationException("Cannot start a Work turn without active task context. " + taskContext.Status);
        }

        bool includeSessionBootstrap = !sessionState.HasAttachedSessionBootstrap && sessionBootstrapPolicy.HasPrompt;
        bool includeWorkspaceContext = !sessionState.HasAttachedWorkspaceContext && workspaceContext.HasPrompt;

        StringBuilder prompt = new();
        if (includeSessionBootstrap)
        {
            prompt.AppendLine("Coding Services session bootstrap policy:");
            prompt.AppendLine(sessionBootstrapPolicy.PromptText);
            prompt.AppendLine();
        }

        if (mode == WorkflowTurnMode.Work)
        {
            prompt.AppendLine(taskContext.PromptMarkdown);
            prompt.AppendLine();
            prompt.AppendLine("Work-mode task authority:");
            prompt.AppendLine("- The host-selected Active task above is authoritative for this turn.");
            prompt.AppendLine("- Do not reinterpret the current task from prior transcript content, stale notes, UI selection guesses, or broad search results.");
            prompt.AppendLine("- Do not switch to another task unless the user explicitly changes the Active task through the host workflow.");
            prompt.AppendLine("- The Coding Services host governance attached above remains active even if the selected workspace has no local AGENTS.md file.");
            prompt.AppendLine("- Discovery restarts at the top of every Work turn. Do not carry forward an 'already implemented' conclusion from an earlier turn without fresh governed evidence.");
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
            prompt.AppendLine("- If Coding Services attached context names workspace MCP discovery tools, attempt that governed MCP path before claiming the discovery surface is unavailable.");
            prompt.AppendLine("- In Work mode, do not use shell search, broad task-memory scans, or fallback repo scans before the required governed MCP/task path unless the required MCP/tool path fails.");
            prompt.AppendLine("- In Work mode, never claim that the assigned task is already implemented or already complete until the current turn has refreshed every task file through the governed edit path and reported the resulting edit-session evidence.");
            prompt.AppendLine("- In Work mode, direct watched-source reads, get_edit_session_state by itself, prior turn transcript text, previous runtime output, and remembered code shape are diagnostic hints only. They are not completion evidence.");
            prompt.AppendLine("- In Work mode, do not read or cite runtime artifact folders such as runtime\\watched-solutions\\..., workflow\\history, working, staged, metadata, or task-memory as proof that a watched-source task is already complete. Those folders are workflow artifacts, not authoritative watched-source evidence.");
            prompt.AppendLine("- In Work mode, when task files are supplied by the host task context, treat them as the initial refresh targets for the current turn unless the user explicitly narrows scope.");
            prompt.AppendLine("- In Work mode, if a task file has not gone through current-turn refresh_file or new_file, you must treat its state as unverified for completion purposes.");
            prompt.AppendLine("- In Work mode, report an 'already implemented' outcome only after the current turn has produced governed evidence for each relevant task file, including EditSessionId, watchedFilePath, workingFilePath, and classification.");
            prompt.AppendLine("- In Work mode, if classification is unchanged after refresh_file, that means the watched source and the fresh working candidate match for this turn. That is the only governed basis for a no-op conclusion.");
            prompt.AppendLine("- If the host exposes request_operator_confirmation, use it for bounded yes/no operator questions instead of asking those questions freeform in chat.");
            prompt.AppendLine("- If you ask whether task or agent notes should be updated, prefer request_operator_confirmation when available; otherwise phrase it as a strict yes/no question unless the user asked for broader discussion.");
        }
        else
        {
            prompt.AppendLine("- Treat this as discussion/planning/review by default unless the user explicitly asks for code changes.");
            prompt.AppendLine("- Do not assume durable task context is loaded for this turn.");
            prompt.AppendLine("- If code structure matters, refresh digest or MCP summaries rather than relying on stale transcript context.");
        }

        prompt.AppendLine("- When the user asks for tool results, report the results in the same response after the tool call completes; do not wait for a follow-up prompt.");
        prompt.AppendLine();
        prompt.AppendLine("User request:");
        prompt.AppendLine(userPrompt);

        return new WorkflowTurnEnvelope(
            prompt.ToString(),
            mode,
            includeSessionBootstrap,
            includeWorkspaceContext,
            sessionBootstrapPolicy,
            workspaceContext,
            taskContext);
    }
}
