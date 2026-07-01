# Workflow Notes

This file is the working design surface for workflow behavior in
`C:\CodexAppServerWinForms_corrected`.

Use it for in-progress notes across multiple tasks. When a rule becomes stable,
promote the curated version into `AGENTS.md`.

## Current Baseline

- The app is CWD/workspace based.
- The app is the active Coding Services Blazor control surface around
  `codex app-server`.
- Runtime state belongs under `runtime/`.
- Tasks are the durable workflow/memory model.

## Turn Modes

### Discuss

- `Discuss` is the default turn mode.
- `Discuss` is intended for explanation, planning, review, and general
  workspace conversation.
- `Discuss` currently allows a lightweight indexed workspace/bootstrap summary
  to be included in native turn context.
- `Discuss` does not load durable active-task memory by default.
- The agent may still infer architecture from the injected indexed summary even
  when it does not call MCP tools or inspect source directly.

### Work

- `Work` is the governed implementation mode.
- `Work` must require active task context before the turn is sent.
- `Work` should load compact durable task memory:
  active task label/state, user notes, agent notes, task file refs, and a
  bounded event slice.
- `Work` should keep the solution index volatile and refresh-driven.
- `Work` should carry stricter workflow expectations:
  discovery, proposal, edit/diff, compile, reindex.

## Context Sources

- Native app-server turn context can inject:
  `cwd`, mode, workspace boundary guidance, and optional indexed workspace
  bootstrap context.
- MCP is the live refresh/discovery surface and should be treated separately
  from native prompt injection.
- Durable task memory comes from the task board database plus task-memory
  markdown files.

## Service Boundaries

- `CodexConnectionService`
  Transport/orchestration only: server lifecycle, thread lifecycle, turn send,
  telemetry, status, permission responses.
- Workspace workflow context service
  Builds volatile indexed workspace/bootstrap prompt context.
- `TaskWorkflowContextService`
  Loads durable active-task memory from DB/files.
- Turn-context composer
  Decides what gets attached for the current turn.
- Session state
  Holds only small per-thread/per-session facts such as workspace root, thread
  bootstrap state, and selected mode baseline.

## Architecture Guardrails

- Do not let `CodexConnectionService` become the workflow-management god object.
- Do not mix durable task-memory loading with volatile index/bootstrap loading
  in one giant service.
- Do not treat indexed workspace summaries as durable memory.
- Do not silently load active task memory in `Discuss`.
- Prefer small explicit services over one master workflow manager.

## Open Questions

- Should `Discuss` always include the lightweight indexed bootstrap summary, or
  should that become optional/configurable later?
- Should `Work` be blocked in the UI when no Active task exists, or should the
  send path remain the enforcement point?
- What is the final contract for end-of-turn agent-note update prompts?
- Should task creation from discussion become a first-class workflow action?
