# Prompt for Codex

Copy and paste this into Codex:

---

**Prompt:**

```
Read the OpenHands proposed plan at: docs/OpenHands_ProposedPlan/OPENHANDS_DIRECTIVE.md

This proposal is for implementing a governed MCP workflow system. It is ADVISORY ONLY.

Before implementing anything:
1. Read docs/OpenHands_ProposedPlan/00_Overview.md to understand the architecture
2. Search the existing codebase to verify implementations don't already exist
3. Generate appropriate unit tests before implementing any code
4. Verify against existing source - many services referenced may already exist

Key search commands:
- grep -r "WorkflowEditService|RoslynEditService" --include="*.cs"
- grep -r "McpServerTool" --include="*.cs"  
- grep -r "WorkflowSessionState" --include="*.cs"

Do NOT implement until you have verified the proposal against existing code and generated tests.
```

---

## Quick Version (copy just this)

```
Read docs/OpenHands_ProposedPlan/OPENHANDS_DIRECTIVE.md. This is ADVISORY ONLY. 
Before implementing: 1) Read 00_Overview.md 2) Search existing code to verify implementations 
don't exist 3) Generate tests. Search commands: grep -r "WorkflowEditService|RoslynEditService" 
--include="*.cs"
```
