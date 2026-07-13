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
- User requests such as "implement", "execute", "do it", or "plan the current task and execute it" do not waive the governed workflow. They authorize progress through the required workflow stages, not skipping proposal, review, merge, or other required gates.
- In governed editing, complete the intended change for the current file in the Working candidate before staging it for review.
- Do not stage partial file work unless the workflow explicitly calls for an intermediate checkpoint.
- Prefer finishing one file cleanly, then moving to the next required file.
- If a task truly requires coordinated multi-file work, stage those files deliberately under one review session after each file-level change is complete enough to review.
- If a task spans multiple files, declare the governed file set up front before staging review.
- Once a multi-file file set is declared, do not fall back to single-file staging for any file in that session.
- For declared multi-file work, use session-level review staging after all declared files have been updated in Working candidates.
- If a task requires coordinated multi-file work, do not silently split it into separate per-file review sessions just because the first file refresh created a file-scoped edit session id.
- Reuse one governed edit session across the whole coherent change when the MCP/tool surface allows it.
- If the available governed MCP flow appears to create a different edit session id per file and no explicit join-or-reuse path is exposed, stop and report that tooling gap before staging any file for review.
- Do not treat "proceed conservatively per file" as permission to bypass the intended single-session governed review shape.

## Freshness Rules

- Treat indexed MCP summaries as stale after source edits.
- Before trusting index-backed structure after edits, build and reindex.
- Use `get_watched_solution_digest` as the freshness gate before reloading deeper product or test summaries.
- If source truth matters more than startup summary context, refresh through MCP or direct source reads instead of relying on transcript residue.

## MCP And Tooling

- The long-term target in this repo is MCP-first workspace discovery and MCP-first governed edits.
- Prefer exposed workspace MCP tools over generic fallback mechanics when capabilities overlap.
- For governed non-C# text files such as `.razor`, prefer `refresh_file` followed by `replace_text_in_file` or `replace_span_in_file`.
- Do not treat the absence of Roslyn symbol tools for Razor as evidence that no governed MCP edit path exists.
- For coherent multi-file governed work, inspect the session ids returned by the governed edit tools and preserve one shared session when possible.
- If a second file returns a different session id than the first file for the same intended change, treat that as a workflow mismatch that must be surfaced, not silently worked around.
- If the target file has already been chosen and no governed file-read MCP is exposed, a narrow `rg`/`grep` or direct file read against that chosen file is an acceptable last-resort discovery aid only.
- Do not use `apply_patch` or other generic write paths for governed Razor/text edits when the harness exposes `replace_text_in_file` or `replace_span_in_file`.
- If the required MCP method does not exist yet, say so plainly and use the best available fallback.
- When a shell or tool action requires runtime approval, prefer the formal approval flow over conversational permission text alone.
- If a tool or command is denied, cancelled, sandboxed, or fails after approval, treat that as an execution result and continue with the best viable fallback unless the user must choose.

## Governed Review Gate

- The staged-review accept/reject decision is a governed gate driven by MCP elicitation. The session-first governed review launch BLOCKS until the operator answers the elicitation; do not expect it to return before the human decides.
- Accept applies the staged change to watched source; decline rejects and leaves source unchanged; cancel leaves it pending. Never work around the gate by calling accept/reject tools to bypass an unanswered elicitation.
- The gate depends on `mcp_elicitations = true` in the granular approval policy (`CodexAppServerClient.CreateApprovalPolicy`). The elicitation uses the same server-request channel as security/sandbox approvals.
- `GovernedReviewCoordinatorService` is dormant; the elicitation path in `HarnessWorkspaceReviewService` (`IReviewElicitor`) is the live gate. See WORKFLOW.md "Governed Review Gate (Elicitation)".

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

For repeatable dual-instance runs, prefer the pinned scripts under `scripts/`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-SelfHost.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-Child.ps1
```

- SelfHost should pin `C:\CodexAppServerWinForms_corrected\CodexAppServerWinForms_corrected.slnx`.
- Child should pin the watched solution for the selected external workspace explicitly with `--CodingServices:WatchedSolutionPath=...`.
- Do not rely on fallback solution discovery when restarting or testing the child instance.

- If you change turn construction, verify it remains CWD/workspace based.
- If you change UI behavior, rebuild and restart the Blazor app before claiming the change is visible.
- If you change MCP, workflow, or index freshness behavior, verify both the runtime behavior and the status/protocol surfaces.
