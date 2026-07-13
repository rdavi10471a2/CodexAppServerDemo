# Codex App Server Blazor Control

Version `1.1` - `workflow established`

This repository contains the Blazor Server control surface for `codex app-server` plus the local Coding Services MCP host used to govern watched-workspace edits.

The old WinForms harness has been retired. The app now centers on a workspace/CWD workflow, governed local Working candidates, session-first review, and post-accept freshness.

## What `1.1` Establishes

- Workspace-first operation instead of selected-file assumptions.
- Host-governed workflow doctrine loaded at turn start.
- MCP-first discovery and MCP-governed watched-source mutation.
- Required edit-session semantics for single-file and multi-file changes.
- Session-first governed review with human accept/reject authority.
- Post-accept refresh/reindex as the normal freshness path.
- Structured yes/no operator confirmation through MCP elicitation.

## Run

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj
```

## Dual Instance Launch

Prefer the pinned launch scripts instead of retyping command lines:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-SelfHost.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-Child.ps1
```

Or start both:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-DualInstance.ps1
```

The child script accepts overrides when targeting a different watched project:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-CodingServices-Child.ps1 `
  -WorkspaceRoot C:\SchemaStudioWebViewer1 `
  -WatchedSolutionPath C:\SchemaStudioWebViewer1\SchemaStudioWebViewer.sln
```

Important:

- Always pin `CodingServices:WatchedSolutionPath` explicitly for non-self-host runs.
- Do not rely on fallback solution discovery when running multiple instances.
- `5205/6278` are reserved for SelfHost.
- `5215/6289` are reserved for Child by convention in this repo.

The default app and MCP ports are configured in `CodexAppServerBlazor/appsettings.json`:

```json
"BlazorHost": {
  "Url": "http://localhost:5205"
},
"Mcp": {
  "Url": "http://localhost:6278"
}
```

Open:

```text
http://localhost:5205/
```

MCP health:

```text
http://localhost:6278/health
```

## Governed Workflow Model

The intended workflow is:

```text
choose CWD -> get current task -> discovery -> refresh/new_file -> governed mutation -> session review -> accept/reject -> post-accept freshness
```

The app is designed so that:

- prompt text alone is not the workflow;
- policy text establishes the doctrine;
- MCP tools provide the governed action surface;
- compiler/index/review outputs provide the freshest truth;
- watched source is mutated through the governed Working/edit path rather than direct generic writes.

This is the practical meaning of:

```text
reason in the cloud; edit locally
```

The model reasons from compact context, but watched-source changes are composed against explicit local Working candidates and promoted through governed review.

## Policy Text Files

The governed workflow is driven by text policy loaded by the host and attached to turns. The main policy files live under [`CodexAppServerBlazor/docs/policy/`](./CodexAppServerBlazor/docs/policy/):

- `CS-SessionBootstrap.txt`
  Host bootstrap doctrine. Establishes session authority, discovery order, edit-session rules, review boundaries, and freshness expectations.
- `CS-EditToolGuidance.txt`
  Decision rules for choosing governed discovery and mutation tools.
- `CS-WorkedExamples.txt`
  Concrete worked patterns for existing-file edits, new-file authoring, multi-file sessions, and governed review.
- `CS-DiagnosticPrompts.txt`
  Reusable prompts for policy and workflow evaluation turns.
- `CS-RepeatablePerformance-Handoff.txt`
  Handoff seed for repeatable-performance work and watched-workspace testing.

Related doctrine files:

- [`AGENTS.md`](./AGENTS.md)
  Host repository workflow rules and architectural boundaries.
- [`WORKFLOW.md`](./WORKFLOW.md)
  Runtime and review-flow behavior notes.

These files are used to create the governed workflow by doing three different jobs:

1. `Session/bootstrap doctrine`
   Defines non-negotiable workflow order, authority, stop rules, and review gates.
2. `Tool decision guidance`
   Tells the model which governed MCP path is the normal choice for each class of change.
3. `Worked examples and prompts`
   Provide concrete execution patterns and evaluation scaffolding without weakening the main doctrine.

## MCP Tool Surface

The local MCP surface is exposed by the host and advertised through `/health`. Tool descriptions are sourced from the live tool classes so the runtime inventory stays aligned with code.

### Workspace Context Tools

- `get_workspace`
  Returns metadata for the selected workspace CWD.
- `get_watched_solution_digest`
  Returns cheap readiness and change-detection metadata.
- `get_watched_solution_summary`
  Returns indexed product-source structure for discovery.
- `get_test_project_summary`
  Returns indexed test-project structure for discovery.
- `get_current_task`
  Returns the current Active task for the selected workspace.
- `rebuild_solution_index`
  Rebuilds the watched-solution index and returns refreshed readiness metadata.

### Governed Edit Session Tools

- `get_edit_session_state`
  Diagnostic read of the current governed edit-session state for one watched file.
- `refresh_file`
  Normal entry point for an existing watched file.
- `new_file`
  Normal entry point for a brand-new watched file.
- `declare_session_files`
  Advanced bulk declaration or replacement of the full governed file set for an existing session.
- `add_file_to_session`
  Normal incremental session-growth path for coherent multi-file work.

### Governed Text/File Mutation Tools

- `replace_text_in_file`
  Normal path for one contiguous unique text replacement.
- `replace_span_in_file`
  Coordinate fallback only.
- `submit_file`
  Whole-file Working-candidate replacement for new files, generated files, or true whole-file intent.

`submit_file` intentionally does not take `sessionId`. The session is established earlier by `refresh_file` or `new_file`, and `submit_file` writes the Working candidate already bound to `watchedFilePath`.

### Roslyn / Semantic C# Tools

- `get_file_outline`
  Returns a Roslyn outline for one C# file.
- `get_source_map`
  Returns a Roslyn source map and nearby structural discovery context.
- `get_symbol`
  Reads one symbol body from the governed Working candidate.
- `submit_symbol`
  Replaces one symbol body and is the normal path for multi-fragment logical edits inside one symbol.
- `add_symbol`
  Generic semantic add path for symbol kinds not covered by narrower typed add tools.
- `add_field`
- `add_property`
- `add_method`
- `add_constructor`
- `add_nested_type`
- `set_type_partial`
- `add_using`
- `remove_using`
- `remove_symbol`

For existing C# source, Roslyn-backed semantic edit tools are the required default for reliable governed editing because they operate on semantic blocks rather than ad hoc text spans.

### Governed Review Tools

- `list_pending_staged_reviews`
  Lists pending staged review records for the selected workspace.
- `load_staged_review`
  Loads a specific staged review model.
- `load_next_session_review`
  Loads the next pending review for an edit session.
- `stage_edit_session_for_review`
  Stages every declared file for one governed review session.
- `accept_staged_review`
  Accepts a staged review record into watched source and runs post-accept refresh behavior.
- `reject_staged_review`
  Rejects a staged review record and leaves watched source unchanged.

### Operator Elicitation Tools

- `request_operator_confirmation`
  Requests a strict yes/no answer from the operator through MCP elicitation.
- `probe_notes_update_elicitation`
  Raises a simple yes/no elicitation for host testing.

## Tooling Defaults

The current doctrine intentionally pushes the agent toward deterministic surfaces:

- Prefer indexed workspace discovery over broad shell search.
- Prefer `refresh_file` / `new_file` before reasoning deeply about file state.
- Prefer semantic C# edits over coordinate edits.
- Prefer `replace_text_in_file` for a single contiguous literal change.
- Treat `replace_span_in_file` as fallback-only.
- Keep one coherent edit session across a coherent multi-file change.
- Stage the session, not isolated files, for governed review.
- Treat accepted review as the normal point where freshness is restored.

## Project Layout

```text
CodexAppServerBlazor/                    Blazor UI, host, policy loading, and app wiring
CodexAppServerBlazor.AICodingServices/   Governed edit, validation, staging, and workflow services
Mcp/                                     Local MCP host and governed tool surface
CodexAppServerClient.cs                  codex app-server JSON-RPC client
CodexEvents.cs                           protocol event models
JsonRpcMessage.cs                        JSON-RPC message model
tests/                                   regression and workflow tests
docs/                                    notes, plans, mockups, and policy references
scripts/                                 repeatable launch helpers
```

## Build And Verify

Use from the repo root:

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj
```

If you change policy or startup doctrine:

- rebuild and restart the Blazor app so the shipped text files are current;
- start a fresh thread if you want the model to reload the new doctrine cleanly;
- use the diagnostic prompts to verify that the model now describes the governed workflow correctly.
