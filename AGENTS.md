# AGENTS.md

## Workspace Model

- This repository is the Coding Services Blazor control surface for `codex app-server`.
- Work in this repo is CWD/workspace based, not selected-file based.
- The CWD chosen in the UI is the authoritative workspace for turns sent through the app.
- Do not reintroduce selected-file workflow assumptions unless a task explicitly requires it.

## Turn Modes

- `Discuss` is the lightweight mode.
- In `Discuss`, default to analysis, planning, review, and context shaping.
- In `Discuss`, do not silently assume durable task memory is loaded.
- `Work` is the governed task mode.
- In `Work`, require active task context before sending the turn.
- Durable workflow memory belongs in task artifacts such as user notes, agent notes, task files, and task events.
- Indexed workspace summaries are lookup context, not durable memory.

## Workflow Order

- Preferred order is: discovery, proposal, edit/diff, compile, reindex.
- Keep changes small, explicit, and easy to verify.
- Prefer MCP/index-backed discovery over broad shell/text search when the needed workspace context is available there.
- Treat repo-local workflow rules as operational requirements, not optional guidance.

## Freshness Rules

- Treat indexed MCP summaries as stale after source edits.
- Before trusting index-backed structure after edits, build and reindex.
- Use `get_watched_solution_digest` as the freshness gate before reloading deeper product or test summaries.
- If source truth matters more than startup summary context, refresh through MCP or direct source reads instead of relying on transcript residue.

## MCP And Tooling

- The long-term target in this repo is MCP-first workspace discovery and MCP-first governed edits.
- Prefer exposed workspace MCP tools over generic fallback mechanics when capabilities overlap.
- If the required MCP method does not exist yet, say so plainly and use the best available fallback.
- When a shell or tool action requires runtime approval, prefer the formal approval flow over conversational permission text alone.
- If a tool or command is denied, cancelled, sandboxed, or fails after approval, treat that as an execution result and continue with the best viable fallback unless the user must choose.

## Architectural Boundaries

- Keep `CodexConnectionService` focused on transport, session orchestration, and turn lifecycle.
- Do not let `CodexConnectionService` become the workflow-management god object.
- Keep UI concerns in Blazor components/pages, MCP concerns in `Mcp/`, and workflow/edit logic in service layers.
- Keep raw protocol visibility intact when changing telemetry or protocol handling.
- Keep context sources visible in the UI or logs where practical.

## Important Repo Areas

- `CodexAppServerBlazor/`: Blazor UI, host, and app wiring.
- `CodexAppServerClient.cs`: JSON-RPC client for `codex app-server`.
- `Mcp/`: local MCP host and workspace-discovery tool surface.
- `CodexAppServerBlazor.AICodingServices/Workflow/`: governed edit, staging, validation, and workflow services.
- `CodexAppServerWinForms_corrected.slnx`: root solution.

## Build And Verify

Use from the repo root:

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj
```

- If you change turn construction, verify it remains CWD/workspace based.
- If you change UI behavior, rebuild and restart the Blazor app before claiming the change is visible.
- If you change MCP, workflow, or index freshness behavior, verify both the runtime behavior and the status/protocol surfaces.
