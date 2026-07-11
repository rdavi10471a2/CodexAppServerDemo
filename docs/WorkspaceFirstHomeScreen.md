# Workspace-First Home Screen

## Intent

Replace the current "connection form plus work surface" first impression with a workspace-first shell.

The new default should feel like:

- a coding console already pointed at a workspace
- task and assistant work in the center
- environment/configuration available on demand
- diagnostics visible when needed, not always competing for space

This keeps Coding Services feeling like an editor harness, not an admin panel.

## Primary Goals

1. Start focused on the active workspace, not the connection form.
2. Hide the left connection panel by default.
3. Move runtime/configuration into an explicit drawer or modal.
4. Preserve the existing governed workflow surfaces without making them the first thing users see.
5. Make room for task, assistant, review, and source work without constant pane fighting.

## Recommended Layout

### Top Bar

Keep a single persistent header row.

Left:
- logo
- `Coding Services`
- active instance/workspace badge if relevant

Center:
- current workspace path
- optional compact status chip row

Right:
- `Run Started At`
- `System Overview`
- `Connection`
- `Browse`
- server state chip

Suggested behavior:
- `System Overview` opens a dialog with live status details.
- `Connection` opens the runtime/configuration drawer or dialog.
- `Browse` stays visible because changing workspace is a first-class action.

### Main Body

Use the full body for work, not for setup.

Recommended shell:

```text
+----------------------------------------------------------------------------------+
| Header: brand | workspace | run started | system overview | connection | browse |
+----------------------------------------------------------------------------------+
| Task strip / current task banner                                                 |
+----------------------------------------------------------------------------------+
| Primary tabs: Tasks | Assistant | Source | Review | Tools                         |
|----------------------------------------------------------------------------------|
|                                                                                  |
| Active tab content                                                               |
|                                                                                  |
|                                                                                  |
+----------------------------------------------------------------------------------+
| Secondary utility rail or status strip (optional, collapsible)                  |
+----------------------------------------------------------------------------------+
```

## Connection Panel Strategy

Do not show the current left panel at startup.

Instead:

- default state: hidden
- open via `Connection` button in the header
- present as one of:
  - right-side drawer
  - large modal dialog
  - command-style settings sheet

Recommendation:

- use a right drawer on desktop
- full-screen dialog on narrower widths

Why this is better:

- it preserves the existing `ConnectionPanel` component
- it avoids stealing permanent width from the work surface
- it matches the fact that connection/runtime settings are occasional, not continuous

## Task Banner

Keep the active task visible outside the task tab.

The current green host-current-task card is directionally right, but it should be tightened into a compact strip.

Recommended content:

- task id
- task title
- turn mode
- current edit session id if present
- quick link: `Open Tasks`

Example:

```text
TASK-0004  Add The Toggle Home Accent Test Button
Mode: Work | Edit Session: edit-b19f... | Source: get_current_task
```

This preserves context without burning vertical space.

## Tab Model

Split tabs into two tiers conceptually, even if they remain one visible row at first.

### Primary Tabs

These are the tabs people should actually live in:

- `Tasks`
- `Assistant`
- `Source`
- `Review`
- `Tools`

### Secondary Tabs

These are operational/diagnostic tabs and should be deemphasized:

- `Tests`
- `Status`
- `Protocol`
- `Telemetry`
- `Debug`

Two good options:

1. Move secondary tabs into a `Diagnostics` tab with nested tabs inside it.
2. Keep them out of the main row entirely and expose them from `System Overview`.

Recommendation:

- short term: create a single `Diagnostics` top-level tab
- long term: move most of that into overview panels and keep only the heavy viewers as nested tabs

## New Review Surface

The governed review experience should remain centered and modal in intent.

Given what we learned from the blocking workflow:

- the review surface should feel like a workflow gate, not another casual tab
- however, it should still render inside the app shell when needed

Recommended presentation:

- normal state: `Review` tab exists but is quiet
- active review state:
  - show a full-height review overlay within the work panel
  - dim the background tab content
  - keep the shell visible enough to prove the app is still alive

This preserves workflow gravity without depending on external browser behavior.

## System Overview Dialog

Replace the misleading `SELFHOST`/instance chip debugging emphasis with an intentional overview action.

Recommended placement:

- top-right header, immediately left of `Connection`

Recommended content:

- instance label
- workspace root
- watched solution path
- server started/stopped
- turn running/idle
- current thread id
- current task id/title
- current edit session id
- pending review session id
- MCP URL
- run started at

Optional actions:

- copy status summary
- open debug tab
- open telemetry tab

This gives the old debugging information a home without burning permanent header real estate.

## Full-Screen Startup

To make the app feel full-screen without the left setup panel:

- keep the top header shallow
- hide the connection panel by default
- let the work panel own nearly the full width
- keep the shell at `100vh`

The current `98vh` cap likely contributes to the slightly cramped feel.

Recommended direction:

- main shell: `min-height: 100vh`
- internal panels: calculate height relative to the header, not arbitrary `98vh`

## Visual Direction

Use the existing light shell, but simplify hierarchy.

Keep:

- current brand colors
- rounded white work surface
- green status success chip

Reduce:

- too many concurrent bordered boxes
- always-visible admin-card look
- duplicate status surfaces

Styling direction:

- quieter chrome
- stronger content grouping
- less visible scaffolding
- more emphasis on the current task and active workspace

## Suggested First Implementation Slice

Do this in order:

1. Hide the left connection panel by default.
2. Add a header `Connection` button that opens it as a drawer or dialog.
3. Replace the large task card with a compact active-task strip.
4. Collapse `Status`, `Protocol`, `Telemetry`, and `Debug` into a `Diagnostics` tab.
5. Add `System Overview` in the top-right header.

That gets most of the UX gain without rewriting the governed workflow.

## Suggested Wireframe

```text
+------------------------------------------------------------------------------------------------------+
| Coding Services | C:\SchemaStudioWebViewer1 | Run Started At: 2026-07-11 10:20:01 | Overview | Conn |
+------------------------------------------------------------------------------------------------------+
| TASK-0004 Add The Toggle Home Accent Test Button                                  Work | Server On   |
+------------------------------------------------------------------------------------------------------+
| Tasks | Assistant | Source | Review | Tools | Diagnostics                                          |
|------------------------------------------------------------------------------------------------------|
|                                                                                                      |
| Active tab                                                                                           |
|                                                                                                      |
|                                                                                                      |
|                                                                                                      |
+------------------------------------------------------------------------------------------------------+
```

## Notes Against Current Code

This design maps cleanly onto the existing `Home` shell:

- `command-bar` remains the header
- `workspace-toolbar` can be simplified into the new top work header
- `ConnectionPanel` can be reused inside a drawer/dialog
- current task banner can replace or tighten the existing host-current-task card
- diagnostics tabs already exist and can be grouped instead of rebuilt

This means the screen can be redesigned incrementally, not as a rewrite.
