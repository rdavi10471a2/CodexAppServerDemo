# Project Dependency Map

This note explains how the two main projects in
`C:\CodexAppServerWinForms_corrected` fit together today, and where MCP/tool
porting work should land next.

## Purpose Split

### `CodexAppServerBlazor`

This is the host application.

- Owns the Blazor UI, tabs, dialogs, and session UX.
- Owns app startup and dependency injection in
  `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Program.cs`.
- Owns the live `codex app-server` connection surface:
  `CodexConnectionService`, protocol/status display, approvals, and turn send.
- Owns workspace/session bootstrap behavior:
  selected CWD, bootstrap policy text, turn mode selection, and startup reload.
- Hosts the local MCP HTTP endpoint through
  `C:\CodexAppServerWinForms_corrected\Mcp\McpHostFactory.cs`.

### `CodexAppServerBlazor.AICodingServices`

This is the backend library.

- Owns solution indexing and query support.
- Owns task-board persistence and archived discussion persistence.
- Owns governed workflow/edit primitives:
  Roslyn symbol edits, staged edits, review decisions, validation, and
  index-refresh logic.
- Does not own the Blazor session UI or the `codex app-server` transport.

## Host Entry Points And Wiring Files

These files define the host-side dependency edges.

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Program.cs`
  DI registration and application startup.
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\CodexAppServerBlazor.csproj`
  Project reference to `CodexAppServerBlazor.AICodingServices` plus linked
  shared-root files.
- `C:\CodexAppServerWinForms_corrected\CodexAppServerClient.cs`
  JSON-RPC transport client for `codex app-server`.
- `C:\CodexAppServerWinForms_corrected\CodexEvents.cs`
  Protocol event models consumed by the host.
- `C:\CodexAppServerWinForms_corrected\JsonRpcMessage.cs`
  Shared JSON-RPC message types.
- `C:\CodexAppServerWinForms_corrected\Mcp\McpHostFactory.cs`
  Local MCP host setup and health endpoint.
- `C:\CodexAppServerWinForms_corrected\Mcp\WorkspaceMcpTools.cs`
  Current MCP tool surface.
- `C:\CodexAppServerWinForms_corrected\Mcp\HarnessWorkspaceContextService.cs`
  Workspace discovery/bootstrap metadata backing those tools.
- `C:\CodexAppServerWinForms_corrected\Mcp\WorkspaceState.cs`
  Selected workspace state for the running instance.

## Current Dependency Direction

The intended direction is:

```text
CodexAppServerBlazor
  -> CodexAppServerBlazor.AICodingServices
```

The host app depends on the backend library for indexing, task state, archived
discussions, and workflow/edit services.

The backend library should stay UI-agnostic. It should not depend on Blazor
components, app tabs, or transport/session widgets.

## Shared Root Files

The current solution still has a small shared-root layer compiled into the
Blazor project through linked files:

- `C:\CodexAppServerWinForms_corrected\CodexAppServerClient.cs`
- `C:\CodexAppServerWinForms_corrected\CodexEvents.cs`
- `C:\CodexAppServerWinForms_corrected\JsonRpcMessage.cs`
- `C:\CodexAppServerWinForms_corrected\Mcp\WorkspaceMcpTools.cs`
- `C:\CodexAppServerWinForms_corrected\Mcp\HarnessWorkspaceContextService.cs`
- `C:\CodexAppServerWinForms_corrected\Mcp\McpHostFactory.cs`
- `C:\CodexAppServerWinForms_corrected\Mcp\WorkspaceState.cs`

These are effectively part of the host/runtime boundary even though they sit at
the repo root.

## AICodingServices Files The Host Already Uses

These are the concrete backend seams already exercised by the Blazor host.

### Index And Workspace Discovery

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\SourceWorkspaceService.cs`
  uses:
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Core\CodingServicesSettings.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\SolutionIndexDatabase.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\SolutionIndexStore.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Indexing\SolutionIndexRebuildService.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardRepository.cs`

### Task Board And Archived Discussions

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Tasks\WorkflowTaskBoardViewService.cs`
  uses:
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Core\CodingServicesSettings.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardRepository.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardModels.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\ArchivedDiscussionRow.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Workflow\Tasks\TaskBoardViewModels.cs`

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Tasks\TaskWorkflowContextService.cs`
  uses:
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Core\CodingServicesSettings.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardRepository.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardModels.cs`

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\ArchivedDiscussions\ArchivedDiscussionService.cs`
  uses:
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Core\CodingServicesSettings.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardRepository.cs`
  - `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\ArchivedDiscussionRow.cs`

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Tasks\TranscriptTaskPromotionService.cs`
  uses the task-board service layer that is built on AICodingServices task
  models.

### UI Files Already Bound To Backend Task Models

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TasksTab.razor`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TasksTab.razor.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TaskNavigator.razor`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TaskNavigator.razor.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TaskWorkspace.razor`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\TaskWorkspace.razor.cs`

These consume `TaskBoardViewModels` through the Blazor service layer.

### Workflow Prompt Composition

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Workflow\WorkspaceWorkflowContextService.cs`
  builds volatile workspace prompt context.
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Tasks\TaskWorkflowContextService.cs`
  builds durable task prompt context from the task board repository.
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Workflow\WorkflowTurnContextComposer.cs`
  combines those sources according to turn mode.

## What MCP Exposes Today

The current MCP surface is intentionally small and discovery-oriented:

- `get_workspace`
- `get_watched_solution_digest`
- `get_watched_solution_summary`
- `get_test_project_summary`

Those tools come from:

- `C:\CodexAppServerWinForms_corrected\Mcp\WorkspaceMcpTools.cs`
- `C:\CodexAppServerWinForms_corrected\Mcp\HarnessWorkspaceContextService.cs`

This is workspace/bootstrap context only. It is not the governed workflow.

## What Exists But Is Not Yet Exposed Through MCP

The richer workflow/edit machinery already exists in
`CodexAppServerBlazor.AICodingServices`, especially under:

- `...\Data\`
- `...\Workflow\`
- `...\Workflow\Tasks\`

Important examples:

- task board persistence and view models
- archived discussion storage
- Roslyn symbol operations
- staged edit validation/review
- post-accept index refresh

Concrete files worth porting from first:

- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardRepository.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\WorkflowTaskBoardModels.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\ArchivedDiscussionRow.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Data\SolutionIndexQueryService.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Workflow\RoslynEditService.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor.AICodingServices\Indexing\SolutionIndexRebuildService.cs`

This is the logical source for future MCP tool expansion.

## Recommended Porting Boundary

If we want an MD-driven workflow before adding scripted step engines, the clean
path is:

1. Keep session/bootstrap policy and workflow guidance in Markdown/policy files.
2. Keep Blazor responsible for choosing workspace, mode, task, and user intent.
3. Expose small, explicit MCP tools from existing AICodingServices primitives.
4. Let the prompt/MD policy steer when each tool should be used.
5. Avoid building a large scripted workflow coordinator unless the MD-first
   approach proves insufficient.

## Near-Term MCP Candidates

The safest next MCP additions are narrow tools that map to existing services:

- read active task context
- list archived discussions
- read archived discussion content
- query symbol/file summaries from the solution index
- submit scoped Roslyn symbol edits through typed operations

That gives the agent better structured options without turning the host app into
one large workflow script engine.
