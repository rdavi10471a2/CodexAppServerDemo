# Governed Harness Comparables

Date: 2026-07-11

## Goal

Capture nearby implementations and design patterns for a Coding Services style harness:

- agent host UI
- governed edit sessions
- human review gates
- approval / elicitation flow
- safe but productive coding workflow

This is not a list of exact matches. It is a borrow list.

## Bottom Line

There does not appear to be a public GitHub project that matches the current system exactly:

- Codex or Claude driven
- MCP-governed edits
- explicit edit session identity
- human review queue
- blocking review gate
- post-accept build/reindex continuation

The closest useful references split into three categories:

1. local prior art from the older `CodexUI`
2. public harnesses with strong workflow/governance ideas
3. thin-host wrappers that show good architectural restraint

## Best Direct Reference: old CodexUI

Local reference:

- `C:\VSCodeProjects\CodingServices\src\CodexUI\Components\Pages\StagedReviewDemo.razor`
- `C:\VSCodeProjects\CodingServices\src\CodexUI\Services\StagedReviewPageService.cs`

Why it matters:

- It already used a session-oriented review concept.
- `LoadNextForSession(sessionId)` is the right mental model for queue drain.
- It had an explicit session-complete model.
- Review decisions advanced the queue instead of treating every file as a separate final event.
- Index refresh could be deferred until the session outcome was known.

Patterns worth keeping:

- one review session for one coherent task change
- one queue drain surface
- explicit session-complete state
- accept/reject advancing through remaining session items
- post-session cleanup and refresh, not per-file cleanup by default

Important correction:

- These ideas are no longer just aspirational in the current repo.
- The current `CodexAppServerWinForms_corrected` implementation already uses session-first governed review semantics in the live path.
- Old `CodexUI` remains useful as prior art, but the current system has already adopted the core session model.

Current-code confirmation:

- single-file governed staging is retired in the live review service
- governed review is driven through `stage_edit_session_for_review`
- session queue drain uses `LoadNextForSession(...)`
- session-complete is an explicit terminal model state
- post-accept refresh can be deferred while other records in the session remain pending

Current implementation references:

- `C:\CodexAppServerWinForms_corrected\Mcp\HarnessWorkspaceReviewService.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Services\Workflow\StagedReviewPageService.cs`
- `C:\CodexAppServerWinForms_corrected\CodexAppServerBlazor\Components\Pages\Home\Tasks\StagedReviewDialog.razor`

## Public Comparable: claude-code-harness

Repository:

- `https://github.com/Chachamaru127/claude-code-harness`

Why it is relevant:

- Strong workflow-first thinking
- clear Plan -> Work -> Review framing
- uses committed control artifacts instead of pure prompt improvisation
- treats validation as part of workflow, not as a side note

Patterns worth borrowing:

- workflow phases as product behavior, not just prompt suggestions
- explicit plan/review boundaries
- persistent control artifacts for the agent
- rerunnable, observable workflow steps

## Public Comparable: powerball-harness

Repository:

- `https://github.com/tim-hub/powerball-harness`

Why it is relevant:

- Strong runtime governance ideas
- maintenance and cleanup are first-class concepts
- guardrails are explicit

Patterns worth borrowing:

- cleanup as a formal workflow step
- declarative guardrails
- runtime artifact discipline
- observable workflow state transitions

## Public Comparable: codex-web

Repository:

- `https://github.com/0xcaff/codex-web`

Why it is relevant:

- Shows the value of a thin wrapper over Codex
- good reminder not to fight upstream behavior unnecessarily

Patterns worth borrowing:

- keep the host thin where possible
- let Codex remain the execution engine
- keep wrapper code focused on UX, orchestration, and visibility

## Public Comparable: CodexBridge

Repository:

- `https://github.com/Gan-Xing/CodexBridge`

Why it is relevant:

- Very good architecture instincts for an adapter layer
- keeps Codex as the primary engine and the host as the bridge

Patterns worth borrowing:

- adapter boundaries
- protocol and thread state as source of truth
- avoid duplicating engine responsibilities in the host

## OpenAI Codex Reference

Useful upstream references:

- `https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md`
- `https://github.com/openai/codex/issues/14192`

Why they matter:

- approval semantics and app-server behavior matter for host UX
- some of the strange behavior seen in the harness is better understood as app-server / approval-channel behavior, not purely a UI bug

## What Looks Original Here

The following still appears fairly custom / novel:

- MCP elicitation opening a governed review gate in the host UI
- blocking the agent until that review gate resolves
- draining a multi-file review session through a human-controlled queue
- resuming the turn for post-accept build/reindex

That means we should expect to borrow ideas, not drop in an exact existing implementation.

## Current Design Guidance

### Adopt now

- Keep review session identity central.
- Preserve the existing session-first semantics; do not regress into file-first review behavior.
- Treat one coherent task edit as one review session.
- Drain queue from one review experience.
- Defer final reindex/refresh until the review session is resolved.
- Make phase logging explicit:
  - session created
  - files declared
  - review launched
  - validation passed/failed
  - override required
  - accepted/rejected
  - post-accept build started
  - post-accept build finished

### Defer for later

- broader UI polish
- richer plan/spec generation
- dead-code analysis integration
- advanced queue visualization

### Avoid

- per-file review sessions for a coherent multi-file task
- generic direct writes into watched source
- hiding validator ambiguity
- duplicating Codex engine responsibilities in the host

## Practical Conclusion

The strongest borrow path is:

1. behavior from old `CodexUI`
2. governance discipline from `claude-code-harness` and `powerball-harness`
3. architectural restraint from `codex-web` and `CodexBridge`

The governed review gate itself still appears to be mostly custom work in this repo.
