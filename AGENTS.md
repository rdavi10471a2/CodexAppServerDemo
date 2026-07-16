# AGENTS.md

## Live Tool Surface First

- The governed Coding Services tools for a watched-workspace session live on the harness MCP surface exposed by the host.
- Before claiming that governed task, discovery, edit, review, or operator-confirmation tools are unavailable, perform one broad live discovery pass for the current session's harness MCP tool surface and read the current tool descriptions.
- At the start of each governed Work turn, consult the harness health inventory at `http://localhost:6289/health` and treat its tool list as the authoritative broad inventory for the active harness MCP host.
- Do not claim that the harness edit surface is missing or that the session is effectively read-only until that live discovery pass is complete.
- `tool_search` is not a broad inventory primitive. Use it only as a narrow follow-up to inspect or confirm details for a specific already-known tool before invoking it.
- Do not infer tool absence from a partial, ranked, convenience, or query-filtered subset. If the first result looks incomplete, broaden discovery before making any statement about missing tools.
- The harness health inventory is the de facto truth source for broad harness tool inventory because it is emitted directly by the active MCP host registration.

## Workspace Model

- This repository is the Coding Services Blazor control surface for `codex app-server`.
- Work in this repo is CWD/workspace based, not selected-file based.
- The CWD chosen in the UI is the authoritative workspace for turns sent through the app.
- Do not reintroduce selected-file workflow assumptions unless a task explicitly requires it.
- For governed sessions, treat the host bootstrap/session policy together with this host `AGENTS.md` as the controlling rule set unless selected-workspace policy adds stricter local rules.

## Task Context

- Governed implementation work in this repo requires active task context before the turn is sent.
- Durable workflow memory belongs in task artifacts such as user notes, agent notes, task files, and task events.
- Indexed workspace summaries are lookup context, not durable memory.
- When adding a task directly to the current watched-workspace board, use the live `runtime\watched-solutions\...\planning\board.sqlite` for that workspace, create the task in state `Proposed` for the board's `New Task` queue unless the user asked for another state, create the companion `task-memory` user note plus an empty agent note file, add the normal `Created`/`StateChanged`/`DetailsUpdated`/`NotesUpdated` events, and advance `workflow_task_sequences.name = 'task'` if needed.

## Workflow Order

- Reason in the cloud; edit locally. (Discover and plan through MCP/context first; compose watched-source changes only through governed local Working/edit tools.)
- Treat governed editing as RPC against typed local artifacts, not as freeform file manipulation. Pick the smallest governed operation that matches the intended semantic unit of change.
- Preferred order is: discovery, proposal, edit/diff, compile, reindex.
- When governed validation or review runs, report the validation/build result plainly in chat instead of leaving it implicit in staged metadata.
- Keep changes small, explicit, and easy to verify.
- Prefer MCP/index-backed discovery over broad shell/text search when the needed workspace context is available there.
- Direct watched-source reads are support-only after target selection; they are not a normal substitute for governed discovery or post-refresh structure reads.
- Treat repo-local workflow rules as operational requirements, not optional guidance.
- User requests such as "implement", "execute", "do it", or "plan the current task and execute it" do not waive the governed workflow. They authorize progress through the required workflow stages, not skipping proposal, review, merge, or other required gates.
- For governed task work, get the active task through MCP before treating any task as authoritative for the turn.
- Host-supplied task summaries are startup context, not a substitute for the governed `get_current_task` call during an actual Work turn.
- Do not infer the active task from stale transcript memory, task numbers, runtime artifacts, or filenames when the governed task surface is available.
- In governed editing, complete the intended change for the current file in the Working candidate before staging it for review.
- Do not stage partial file work unless the workflow explicitly calls for an intermediate checkpoint.
- Prefer finishing one file cleanly, then moving to the next required file.
- If a task truly requires coordinated multi-file work, stage those files deliberately under one review session after each file-level change is complete enough to review.
- If a task spans multiple files, make the governed file set explicit under one shared session before staging review.
- The normal incremental path is: the first file establishes the session through `refresh_file` or `new_file`, later files join that session, then `add_file_to_session` grows the declared set one file at a time.
- Use `declare_session_files` only for intentional bulk declaration or replacement of the full non-empty file set, not as the normal way to grow a session.
- Once a multi-file file set is declared, do not fall back to single-file staging for any file in that session.
- For declared multi-file work, use session-level review staging after all declared files have been updated in Working candidates.
- If a task requires coordinated multi-file work, do not silently split it into separate per-file review sessions just because the first file refresh created a file-scoped edit session id.
- Reuse one governed edit session across the whole coherent change when the MCP/tool surface allows it.
- If the available governed MCP flow appears to create a different edit session id per file and no explicit join-or-reuse path is exposed, stop and report that tooling gap before staging any file for review.
- Do not treat "proceed conservatively per file" as permission to bypass the intended single-session governed review shape.

## Freshness Rules

- Treat indexed MCP summaries as stale after source edits.
- Accepted governed review is the normal point where post-accept build/digest/index freshness is restored.
- Do not trigger a second explicit rebuild/reindex by default after accept unless the post-accept refresh failed, was deferred, or you are intentionally in a recovery/diagnostic path.
- Use `get_watched_solution_digest` as the freshness gate before reloading deeper product or test summaries.
- If source truth matters more than startup summary context, refresh through MCP or direct source reads instead of relying on transcript residue.
- Before claiming a task is already implemented, produce fresh governed refresh evidence for each task file in the current turn.
- Runtime workflow artifacts under `runtime\watched-solutions\...` are not authoritative proof that watched source is already correct.
- After `refresh_file` or `new_file`, treat the governed Working candidate plus governed structure tools as the primary local context source for the turn. Reason primarily from the refreshed Working candidate and governed structure tools. Do not fall back to broad watched-source reads or shell search unless the governed discovery surface is genuinely insufficient for the next step.

## MCP And Tooling

- The governed edit tools exist to let the agent reason from compact cloud context while composing watched-source changes locally through the harness-owned workflow.
- The long-term target in this repo is MCP-first workspace discovery and MCP-first governed edits.
- Prefer exposed workspace MCP tools over generic fallback mechanics when capabilities overlap.
- At the start of each governed Work turn, do one broad live tool-surface discovery pass before choosing the mutation path.
- Use the harness health inventory as that broad discovery pass and treat it as the authoritative callable inventory for the turn.
- If the first discovery pass was narrow, ranked, or filtered, broaden it before making any claim that a governed tool family is missing.
- Do not treat one narrow or relevance-filtered `tool_search` result as a complete callable inventory.
- Use `tool_search` only after that health-backed inventory step, and only to inspect or confirm details for a specific already-known tool before using it, not to determine whether a tool family exists.
- Discovery should narrow in phases: derive the candidate file set from the task, confirm the target through governed discovery, then refresh into Working before deeper reasoning or mutation.
- For governed non-C# text files such as `.razor`, prefer `refresh_file` followed by `replace_text_in_file`; use `replace_span_in_file` only as the last governed text-edit choice when the safer text replacement path cannot express the change cleanly.
- For governed C# files, prefer Roslyn or symbol-aware MCP edits when the tool surface supports the intended change.
- For governed existing C# files, Roslyn-backed semantic edit tools are the required default for reliable structured edits. They establish reliable semantic edit boundaries, provide deterministic block-level replacement behavior, and improve diff stability for governed merge review.
- Prefer replacing, adding, or removing semantic blocks such as methods, properties, fields, constructors, classes, nested types, and using directives over text pokes, coordinate edits, or whole-file rewrites.
- Use `replace_text_in_file` for the simplest contiguous unique replacement only.
- Use `replace_span_in_file` only as coordinate fallback.
- Do not use `submit_file` for an existing C# file unless the task is genuinely whole-file in scope or no narrower governed tool can express the change safely.
- Do not treat the absence of Roslyn symbol tools for Razor as evidence that no governed MCP edit path exists.
- For coherent multi-file governed work, inspect the session ids returned by the governed edit tools and preserve one shared session when possible.
- If a second file returns a different session id than the first file for the same intended change, treat that as a workflow mismatch that must be surfaced, not silently worked around.
- If the target file has already been chosen and no governed file-read MCP is exposed, a narrow `rg`/`grep` or direct file read against that chosen file is an acceptable last-resort discovery aid only.
- Once the target file has been refreshed and governed structure tools such as `get_file_outline`, `get_source_map`, or `get_symbol` are available, prefer those over direct shell reads of watched source.
- Do not use `apply_patch` or other generic write paths for governed Razor/text edits when the harness exposes `replace_text_in_file` or `replace_span_in_file`.
- Do not use generic direct-write fallbacks against watched source when a governed MCP mutation path exists.
- If the host exposes `request_operator_decision`, use it for bounded yes/no operator questions instead of asking those questions freeform in chat.
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

For repeatable app runs, prefer the pinned script under `scripts/`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices.ps1
```

- Pin the watched solution for the selected external workspace explicitly with `--CodingServices:WatchedSolutionPath=...`.
- Do not rely on fallback solution discovery when restarting or testing the app.

- If you change turn construction, verify it remains CWD/workspace based.
- If you change UI behavior, rebuild and restart the Blazor app before claiming the change is visible.
- If you change MCP, workflow, or index freshness behavior, verify both the runtime behavior and the status/protocol surfaces.
