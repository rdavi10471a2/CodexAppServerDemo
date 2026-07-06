# Codex Verified Implementation Plan

Status: advisory review of the OpenHands proposal against the current repository on July 5, 2026.

This document is the repo-verified follow-up to `OPENHANDS_DIRECTIVE.md` and `00_Overview.md`. It is intentionally narrower than the OpenHands proposal and reflects what already exists in `C:\CodexAppServerWinForms_corrected` today.

## Pull And Verification Result

- `git fetch origin` completed successfully.
- `git rev-list --left-right --count HEAD...origin/codex/review-workspace-checkpoint` returned `0 0`.
- There was no newer remote source to pull before this review.

## What Already Exists

The proposal is directionally useful, but several of its "new" items are already present in this repo.

### Existing workflow/session seams

- `CodexAppServerBlazor/Services/Workflow/WorkflowSessionState.cs`
  - already exists
  - currently minimal
  - already used by `CodexConnectionService`
- `CodexAppServerBlazor/Services/Workflow/WorkflowTurnContextComposer.cs`
  - already exists
  - already enforces `Work` requiring task context
  - already composes session bootstrap, workspace context, and mode-specific rules
- `CodexAppServerBlazor/Services/CodexConnectionService.cs`
  - already owns thread start, turn send, session bootstrap attachment, workspace context attachment, and current `WorkflowSessionState` lifecycle

### Existing governed edit backend

- `CodexAppServerBlazor.AICodingServices/Workflow/WorkflowEditService.cs`
  - already implements working-candidate session management
  - already implements file refresh/new-file flows
  - already implements text and span replacement
  - already implements staging/accept/reject/index-fresh flows
- `CodexAppServerBlazor.AICodingServices/Workflow/RoslynEditService.cs`
  - already implements Roslyn-backed semantic edit operations
  - includes:
    - `SubmitSymbol`
    - `AddField`
    - `AddProperty`
    - `AddMethod`
    - `AddConstructor`
    - `AddNestedType`
    - `RemoveSymbol`
    - `AddUsing`
    - `RemoveUsing`
    - source-map and outline reads

### Existing MCP surface

- `Mcp/WorkspaceMcpTools.cs` already exists, but today it only exposes workspace/index discovery:
  - `GetWorkspace`
  - `GetWatchedSolutionDigest`
  - `GetWatchedSolutionSummary`
  - `GetTestProjectSummary`

This confirms the main gap is not "invent governed edit services"; it is "expose and govern the existing services correctly."

## What Does Not Yet Exist

These are the proposal areas that still appear genuinely missing in the current repo.

### Missing MCP wrappers for the edit backend

No current MCP tool surface exposes:

- semantic Roslyn edits
- working-file refresh/new-file operations
- replace-text or replace-span operations
- session/status inspection for those edit sessions

### Missing workflow-state layer for governed MCP editing

There is no current repo-verified implementation of:

- a multi-file edit-session model attached to `WorkflowSessionState`
- explicit workflow phases such as discovery, editing, overlay build, merge pending, rejection loop
- a bridge that converts MCP edit activity into governed workflow state
- a workflow tool manifest or capability manifest returned to the model

### Missing test coverage

Current tests cover:

- turn composition
- task workflow context
- Codex connection sequencing
- task board behavior
- archived discussions

Current tests do not appear to cover:

- `WorkflowEditService`
- `RoslynEditService`
- MCP wrappers for governed edits
- workflow phase transitions for a future edit-session state machine

## Corrections To The OpenHands Proposal

These are the main proposal adjustments I recommend before implementation.

### 1. Do not put new edit-session models under `CodexAppServerBlazor/Services/Workflow/`

The repo already separates UI/session orchestration from governed edit backend work.

Recommended split:

- backend edit-session models and analyzers that are about governed edit mechanics should live with the backend workflow/edit stack under:
  - `CodexAppServerBlazor.AICodingServices/Workflow/`
- UI/session-only coordination models that are about prompt/session state may live under:
  - `CodexAppServerBlazor/Services/Workflow/`

If we put everything in the Blazor host project, we will blur the exact service boundary that `AGENTS.md` says to preserve.

### 2. Do not let `CodexConnectionService` become the state machine

`CodexConnectionService` already owns:

- transport
- thread lifecycle
- permission plumbing
- telemetry/status surfaces
- turn dispatch

It should not absorb fine-grained workflow-phase rules. If we add governed MCP editing, the connection service should delegate to a dedicated coordinator and only feed it lifecycle events.

### 3. Do not add MCP wrappers before tests exist

The proposal's order should be adjusted. The first executable step should be test scaffolding around the backend we already have.

### 4. Do not assume one giant workflow rollout

The proposal is too broad for one pass. The safer path is:

1. verify and test the backend edit primitives
2. expose a minimal MCP wrapper set
3. add read-only capability/state tools
4. only then add prompt guidance and phase governance

## Recommended Test-First Plan

This is the implementation order I recommend.

### Phase 0: Freeze the contract in tests

Add new tests before any new production workflow code:

- `WorkflowEditServiceTests`
  - refresh existing file
  - new file session
  - replace text with expected match counts
  - replace span with hash guards
  - accept/reject transitions
  - index-stale to index-fresh behavior
- `RoslynEditServiceTests`
  - submit symbol replacement
  - add method/property/field
  - remove symbol
  - add/remove using
  - invalid selector and unsupported file scenarios
- `WorkspaceMcpTools` or future MCP wrapper tests
  - verify wrapper delegates to backend service
  - verify request/response shaping
  - verify file-path normalization and error behavior

This phase should produce tests only.

### Phase 1: Expose existing backend through minimal MCP wrappers

Add MCP wrappers for the backend services we already have, without introducing a full governed state machine yet.

Recommended first wrapper set:

- read/status
  - edit session status
  - source map
  - file outline
  - symbol read
- file/session operations
  - refresh file
  - new file
- edit operations
  - submit symbol
  - add field
  - add property
  - add method
  - add constructor
  - remove symbol
  - add using
  - remove using
  - replace text
  - replace span

Reason:

- this delivers the MCP-first editing objective quickly
- it validates the wrapper layer before any larger workflow-state design
- it keeps failure scope small and observable

### Phase 2: Add capability classification

Only after wrappers work, add a file-capability analyzer that tells the model which edit tools are appropriate for a path.

This is where hybrid-file handling should be scoped carefully:

- `.cs` can prefer semantic Roslyn tools
- `.razor`, `.css`, `.js`, `.json`, `.md` likely need text/file operations
- Razor hybrid handling should start conservative, not ambitious

The first version should guide tool priority, not attempt magical full-file intelligence.

### Phase 3: Extend session state minimally

Extend `WorkflowSessionState` only enough to support:

- whether governed MCP editing is active for the current thread
- a current edit-session summary
- a simple current workflow phase
- lightweight tool usage/capability hints

Avoid a full merge-review state machine in the first pass.

### Phase 4: Add prompt guidance integration

Once wrappers and capability classification are real, update `WorkflowTurnContextComposer` to include compact edit guidance for `Work` turns only.

This guidance should:

- remind the agent which MCP tools are preferred
- discourage broad shell fallback when an MCP tool exists
- stay compact enough not to flood every turn

### Phase 5: Add merge/review governance only after the above is stable

The overlay-build gate, merge-pending review loop, and rejection handling are valid goals, but they should be treated as a second project slice, not bundled into the first MCP wrapper rollout.

## Suggested Implementation Slices

### Slice A: Backend test coverage

Goal:

- add tests for existing edit services

No MCP changes yet.

### Slice B: Minimal edit MCP surface

Goal:

- expose existing backend safely
- prove the model can use MCP edit tools instead of generic file editing

### Slice C: Capability manifest

Goal:

- help the model choose the right tool for each file kind

### Slice D: Session-guidance integration

Goal:

- inject MCP-first workflow guidance into `Work` mode without breaking `Discuss`

### Slice E: Review/merge state machine

Goal:

- layer in the human review and rejection loop after slices A through D are proven

## Files Most Likely To Change

Based on current repo structure, these are the most credible change points.

### Likely production files

- `CodexAppServerBlazor/Program.cs`
  - only for DI registration once new services are real
- `CodexAppServerBlazor/Services/Workflow/WorkflowSessionState.cs`
  - small state extension only
- `CodexAppServerBlazor/Services/Workflow/WorkflowTurnContextComposer.cs`
  - compact guidance integration only after wrappers exist
- `CodexAppServerBlazor/Services/CodexConnectionService.cs`
  - event delegation hooks only, not full workflow logic
- `Mcp/WorkspaceMcpTools.cs`
  - if this stays the shared MCP tool surface

### Likely new backend files

- `CodexAppServerBlazor.AICodingServices/Workflow/FileCapabilitiesAnalyzer.cs`
- possibly a small workflow manifest/composer service in `CodexAppServerBlazor.AICodingServices/Workflow/`
- possibly a thin bridge/coordinator service, but only if tests prove it is needed

### Likely new test files

- `tests/CodexAppServerBlazor.Tests/WorkflowEditServiceTests.cs`
- `tests/CodexAppServerBlazor.Tests/RoslynEditServiceTests.cs`
- `tests/CodexAppServerBlazor.Tests/WorkspaceMcpToolsEditTests.cs`
- future:
  - `tests/CodexAppServerBlazor.Tests/WorkflowStateManagerTests.cs`

## Recommended Near-Term Decision

The best next implementation step is:

1. do not implement the full OpenHands plan as written
2. treat it as a source of feature ideas only
3. start with tests around the existing edit backend
4. expose a small MCP wrapper set for the already-implemented edit operations
5. add state-machine complexity only after those pieces are proven

## Commands Used For This Review

```powershell
git fetch origin
git rev-list --left-right --count HEAD...origin/codex/review-workspace-checkpoint
rg -n "WorkflowEditService|RoslynEditService|McpServerTool|WorkflowSessionState" C:\CodexAppServerWinForms_corrected -g "*.cs"
```

## Bottom Line

The OpenHands proposal is useful as a roadmap, but it overstates how much is missing.

What is already done:

- governed file-edit backend
- Roslyn semantic edit backend
- workspace/session bootstrap flow
- discuss/work turn composition

What is actually missing:

- tests for the edit backend
- MCP wrappers exposing the existing backend
- compact capability guidance
- a carefully scoped workflow-state layer

That narrower plan is the one I recommend implementing.
