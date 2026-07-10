# MCP Workflow Execution Slices

Status: active execution plan derived from `CODEX_VERIFIED_IMPLEMENTATION_PLAN.md`.

This is the concrete, definite sequence to execute in this repository. Each slice is intended to be independently achievable, testable, and reviewable.

## Slice 1: Backend Contract Tests

Goal:

- prove the current governed edit backend works as expected before exposing anything new through MCP

Scope:

- `WorkflowEditService`
- later in the same slice or immediately after: `RoslynEditService`

Deliverables:

- `WorkflowEditServiceTests.cs`
- `RoslynEditServiceTests.cs`

Required test cases:

- refresh existing file session
- create new-file session
- ensure editable session auto-creates refresh/new session
- replace text with expected match count
- replace text mismatch throws
- replace span by line/column
- unsupported or invalid selector scenarios for Roslyn operations
- add/remove symbol scenarios for Roslyn operations

Exit criteria:

- tests pass locally
- no production code changes required unless tests expose real defects

Status:

- complete

## Slice 2: Minimal MCP Edit Surface

Goal:

- expose a small, stable MCP tool contract over the existing backend services

Initial tool set:

- `get_edit_session_state`
- `refresh_file`
- `new_file`
- `replace_text_in_file`
- `replace_span_in_file`
- `get_file_outline`
- `get_symbol`
- `submit_symbol`

Rules:

- each MCP method delegates to existing backend logic
- do not duplicate edit logic in the MCP class
- keep input/output models compact and explicit

Exit criteria:

- tool-level tests exist
- backend tests from Slice 1 remain green

Status:

- in progress

## Slice 3: Roslyn Add/Remove MCP Methods

Goal:

- expose the high-value semantic edit operations one family at a time

Initial method family:

- `add_field`
- `add_property`
- `add_method`
- `add_constructor`
- `remove_symbol`
- `add_using`
- `remove_using`

Exit criteria:

- each method has focused tests
- tool output is stable enough to guide the agent without extra narration

Status:

- pending

## Slice 4: Capability Guidance

Goal:

- tell the agent which tool family should be preferred for a given file

Recommended first-pass behavior:

- `.cs` => prefer Roslyn semantic tools
- `.razor`, `.css`, `.js`, `.json`, `.md` => prefer text/file tools
- unknown files => conservative text/file fallback

Important constraint:

- do not attempt an ambitious full Razor semantic story in the first pass

Exit criteria:

- capability analyzer exists
- tests cover at least `.cs`, `.razor`, `.css`, `.md`

Status:

- pending

## Slice 5: Work-Turn Guidance Injection

Goal:

- inject compact MCP-first editing guidance into `Work` turns only

Rules:

- do not bloat `Discuss`
- do not move workflow ownership into `CodexConnectionService`
- guidance should prefer MCP edit tools when available and mention shell fallback only as fallback

Exit criteria:

- composer tests updated
- prompt remains compact and mode-correct

Status:

- pending

## Slice 6: Workflow State Expansion

Goal:

- add only the minimum session state needed to track an edit session and simple phase

Initial state additions:

- current edit session summary
- current workflow phase
- whether governed MCP editing is active for the thread

Non-goals for this slice:

- full merge-review state machine
- human rejection loop orchestration
- self-restart handling

Exit criteria:

- state tests exist
- `CodexConnectionService` still acts as transport/orchestration, not workflow god object

Status:

- pending

## Slice 7: Human-In-The-Middle Review Loop

Goal:

- add the human review, rejection, and retry loop only after slices 1 through 6 are stable

Scope examples:

- merge-pending state
- rejected-file loop
- overlay-build handoff
- review status surfacing in UI

Status:

- deferred until earlier slices are complete

Known future dependency:

- when this slice is active, review the existing merge/review page implementation in:
  - `C:\VSCodeProjects\CodingServices\src\CodexUI`
- preferred approach is to port the proven merge-page behavior into this repo rather than invent a second merge UX from scratch
- before porting, compare its assumptions against:
  - current task model
  - current Blazor layout
  - current `CodexConnectionService` / MCP event flow

## Recommended Test Workspace Strategy

For now, we do not need a real sample Blazor app for most backend tests.

Use this order:

1. Temporary synthetic workspace
- best for `WorkflowEditService`
- best for most `RoslynEditService` unit tests
- fastest and least brittle

2. Minimal C# workspace with a small solution and 2-3 source files
- best for MCP wrapper tests that need realistic paths and project boundaries
- still lighter than a full Blazor sample

3. Minimal Blazor sample workspace
- only needed once we test:
  - `.razor` capability classification
  - hybrid file guidance
  - workspace/index behavior that depends on Blazor file shapes

Recommendation:

- do not start with a minimal Blazor app
- start with a synthetic temporary workspace and small `.cs` files
- add a minimal Blazor sample later when capability classification reaches `.razor`

Reference test source worth mining later:

- `C:\VSCodeProjects\CodingServices\tests`

Most relevant reference areas identified so far:

- `AICodingServices.Workflow.Tests\WorkflowEditServiceSafetyTests.cs`
  - overlay-validation behavior
  - manifest propagation
  - repo-shaped attributed edit cases
- `AICodingServices.Workflow.Tests\RoslynEditServiceOutlineTests.cs`
  - outline/read patterns
- `AICodingServices.Workflow.Tests\RoslynEditServiceSourceMapTests.cs`
  - source-map/navigation patterns
- `AICodingServices.Indexing.Tests\StagedDecisionWorkflowTests.cs`
  - accept/reject and pre-merge validation workflow ideas
- `CodexUI.Tests\StagedReviewPageServiceTests.cs`
  - later merge/review page port guidance

## Current Next Action

Current execution target:

- implement and verify the first minimal MCP edit surface over the already-tested backend
