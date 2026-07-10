# OpenHands Proposed Plan: Coding Services MCP Workflow

## Overview

This document describes the implementation plan for the Coding Services MCP Workflow, which extends `WorkflowSessionState` to support a governed edit session workflow with:

- Single edit session (multiple files)
- Semantic MCP tools replacing Codex native `grep`/`ApplyPatch`
- Overlay build quality gate
- Human-in-the-loop merge with rejection handling
- Task state persistence across workflow phases
- File capability detection (including hybrid Razor files)

---

## Table of Contents

1. [Architecture Summary](#architecture-summary)
2. [Source Files](#source-files)
3. [Workflow Phases](#workflow-phases)
4. [MCP Tool Mapping](#mcp-tool-mapping)
5. [Reference Implementation](#reference-implementation)
6. [Open Items](#open-items)

---

## Architecture Summary

### Current State

```
CodexConnectionService
    └── WorkflowSessionState (minimal)
    └── WorkflowTurnContextComposer
    └── TaskWorkflowContextService
    └── WorkspaceWorkflowContextService
```

### Target State

```
CodexConnectionService
    └── WorkflowSessionState (extended)
    │       ├── CurrentEditSession
    │       ├── CurrentPhase
    │       ├── MergeReviewState
    │       └── ToolUsageTracker
    ├── WorkflowTurnContextComposer
    │       └── ComposeEditSessionGuidance()
    ├── TaskWorkflowContextService
    ├── WorkspaceWorkflowContextService
    ├── FileCapabilitiesAnalyzer (NEW)
    ├── WorkflowStateManager (NEW)
    └── EditSessionBridge (NEW)
```

---

## Source Files

### Reference Implementation (Already Exists)

| Folder | Purpose |
|--------|---------|
| `reference_mcp_server/` | Complete MCP server implementation to integrate |

### New Source Files

| File | Purpose |
|------|---------|
| `01_EditSessionModels.cs.txt` | Data models: EditSession, EditSessionFile, MergeReviewState, WorkflowPhase enum |
| `02_FileCapabilitiesAnalyzer.cs.txt` | Detects file kind and allowed tools per file |
| `03_WorkflowStateManager.cs.txt` | Manages workflow state transitions and edit session lifecycle |
| `04_EditSessionGuidanceComposer.cs.txt` | Generates tool priority guidance for prompts |
| `05_EditSessionBridge.cs.txt` | Bridges MCP events to state manager |
| `06_WorkflowSessionState_Modification.cs.txt` | **MODIFICATION**: Add extension properties to existing class |
| `07_WorkflowTurnContextComposer_Modification.cs.txt` | **MODIFICATION**: Add edit session guidance to prompts |
| `08_CS-SessionBootstrap_Modification.txt` | **MODIFICATION**: Add MCP-first workflow policy |
| `09_WorkspaceMcpTools_Additions.cs.txt` | **MODIFICATION**: Add new workflow MCP tools |
| `10_CodexConnectionService_Modification.cs.txt` | **MODIFICATION**: Integrate state manager and bridge |

### Target Locations

| Proposed File | Target Location |
|---------------|-----------------|
| `01_EditSessionModels.cs.txt` | `CodexAppServerBlazor/Services/Workflow/` |
| `02_FileCapabilitiesAnalyzer.cs.txt` | `CodexAppServerBlazor/Services/Workflow/` |
| `03_WorkflowStateManager.cs.txt` | `CodexAppServerBlazor/Services/Workflow/` |
| `04_EditSessionGuidanceComposer.cs.txt` | `CodexAppServerBlazor/Services/Workflow/` |
| `05_EditSessionBridge.cs.txt` | `CodexAppServerBlazor/Services/Workflow/` |
| `06_WorkflowSessionState_Modification.cs.txt` | Edit `CodexAppServerBlazor/Services/Workflow/WorkflowSessionState.cs` |
| `07_WorkflowTurnContextComposer_Modification.cs.txt` | Edit `CodexAppServerBlazor/Services/Workflow/WorkflowTurnContextComposer.cs` |
| `08_CS-SessionBootstrap_Modification.txt` | Append to `CodexAppServerBlazor/docs/policy/CS-SessionBootstrap.txt` |
| `09_WorkspaceMcpTools_Additions.cs.txt` | Edit `CodexAppServerBlazor/Mcp/WorkspaceMcpTools.cs` |
| `10_CodexConnectionService_Modification.cs.txt` | Edit `CodexAppServerBlazor/Services/CodexConnectionService.cs` |

---

## Workflow Phases

```
┌─────────────────────────────────────────────────────────────────────┐
│  TURN START                                                          │
│  User submits prompt → Agent receives with task context               │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: DISCOVERY                                                   │
│  Agent analyzes workspace, proposes edit session                    │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: EDITING                                                     │
│  Agent edits files via MCP tools                                    │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: OVERLAY BUILD                                               │
│  Build runs against overlay                                          │
│  - If FAIL → Agent notified, returns to EDITING                      │
│  - If SUCCESS → Proceed to MERGE                                    │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: MERGE PENDING                                               │
│  Human reviews each file                                             │
│  - ACCEPT → File merged                                             │
│  - REJECT → File flagged, discussion follows                       │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  REJECTION HANDLING                                                 │
│  Discussion → new turn → EDITING for rejected file(s)              │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: MERGE COMPLETE → REBOOT                                    │
│  Build exe, stop, restart                                          │
└─────────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE: QA → Next Turn                                              │
└─────────────────────────────────────────────────────────────────────┘
```

---

## MCP Tool Mapping

### Discovery Tools (Replace grep)

| Need | MCP Tool | Returns |
|------|----------|---------|
| Find symbols | `find_indexed_symbols` | C# symbols with locations |
| Find references | `find_indexed_references` | Where symbol is used |
| Find callers | `find_indexed_callers` | Who calls a method |
| Project structure | `get_solution_index_tree` | File/folder tree |
| Scope queries | `query_solution_index` | Files by namespace/folder |
| Locate files | `find_file` | Files by pattern |

### Edit Tools (Replace ApplyPatch)

| Need | MCP Tool | For |
|------|---------|-----|
| Replace symbol | `submit_symbol` | .cs - symbol changes |
| Add members | `add_field/property/method/constructor` | .cs - new members |
| Remove symbol | `remove_symbol` | .cs - delete |
| Manage usings | `add_using` / `remove_using` | .cs - directives |
| Replace text | `replace_text_in_file` | All files - text |
| Full file | `submit_file` | All files - last resort |
| Refresh file | `refresh_file` | Existing files |
| New file | `new_file` | New files |

### Workflow Tools (NEW)

| Tool | Purpose |
|------|---------|
| `analyze_file_capabilities` | Get file kind and allowed tools |
| `get_edit_session_state` | Current edit session status |
| `request_overlay_build` | Trigger overlay build |
| `get_workflow_tool_manifest` | Get tool priority manifest |

### Existing Backend (NOT Yet Exposed via MCP)

The following services exist in `AICodingServices/Workflow/` but are NOT exposed via MCP:

| Backend Method | Return Type | Status |
|---------------|-------------|--------|
| `SubmitSymbol` | RoslynEditResult | ✅ Implemented, ❌ Not exposed |
| `AddField/Property/Method` | RoslynEditResult | ✅ Implemented, ❌ Not exposed |
| `RemoveSymbol` | RoslynEditResult | ✅ Implemented, ❌ Not exposed |
| `AddUsing/RemoveUsing` | RoslynEditResult | ✅ Implemented, ❌ Not exposed |
| `ReplaceText` | ReplaceTextResult | ✅ Implemented, ❌ Not exposed |
| `SubmitFile/Refresh/NewFile` | EditSessionStatus | ✅ Implemented, ❌ Not exposed |

**The backend services already exist. The proposal is to:**
1. Add MCP tool wrappers to expose these existing services
2. Add the workflow state management (new)
3. Add the guidance/prompt integration (new)

---

## Reference Implementation

The complete MCP server implementation that this proposal builds upon is located at:

```
reference_mcp_server/*.cs.bak
```

Key capabilities in the reference implementation:
- **Semantic Search** (Indexing tools): `find_indexed_symbols`, `find_indexed_references`, `find_indexed_callers`, `query_solution_index`
- **Semantic Edit** (Roslyn tools): `submit_symbol`, `add_field`, `add_property`, `add_method`, `add_constructor`, `remove_symbol`, `add_using`, `remove_using`
- **Text Edit** (Workflow tools): `submit_file`, `replace_text_in_file`, `replace_span_in_file`, `find_text_span`
- **File Management**: `refresh_file`, `new_file`, `get_file`, `find_file`, `check_file_hash`
- **Workflow**: `start_monitor_session`, `stage_candidate_for_review`, `record_diff_decision`, `launch_staged_diff`

---

## Open Items

| Item | Status | Notes |
|------|--------|-------|
| Self-edit detection | Proposed | Needs runtime verification |
| VS restart for self-edit | To work out | Depends on development setup |
| Task state machine | Proposed | InProgress → Editing → Merging → Done |
| Blazor merge page | Not implemented | Future work |
| Feature completion | To determine | How to close a feature |
| Overlay build integration | Proposed | Integration with MSBuild/DotNetBuildRunner |
| DI Registration | Proposed | Add to Program.cs |

---

## Implementation Priority

### Phase 1: Core Models and Analysis
1. `01_EditSessionModels.cs` - Data models
2. `02_FileCapabilitiesAnalyzer.cs` - File analysis
3. `06_WorkflowSessionState_Modification.cs` - Add properties

### Phase 2: State Management
4. `03_WorkflowStateManager.cs` - State transitions
5. `04_EditSessionGuidanceComposer.cs` - Guidance text
6. `05_EditSessionBridge.cs` - MCP bridge
7. `07_WorkflowTurnContextComposer_Modification.cs` - Prompt integration

### Phase 3: MCP Tools
8. `09_WorkspaceMcpTools_Additions.cs` - Expose backend services

### Phase 4: Service Integration
9. `10_CodexConnectionService_Modification.cs` - Wire everything together
10. Update `Program.cs` for DI registration
11. `08_CS-SessionBootstrap_Modification.txt` - Policy updates
