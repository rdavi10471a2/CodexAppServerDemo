# OpenHands Proposed Plan: Coding Services MCP Workflow

## Location

```
docs/OpenHands_ProposedPlan/
```

## Purpose

This folder contains an **OpenHands-generated implementation proposal** for extending the CodexAppServerBlazor codebase with a governed MCP workflow system.

## Status: ADVISORY ONLY

**This proposal is informational and requires verification before implementation.**

Do NOT implement anything from this proposal without:
1. Reading the existing source code thoroughly
2. Verifying the proposal against current architecture
3. Generating appropriate unit/integration tests
4. Getting explicit approval

## Files in This Folder

| File | Purpose |
|------|---------|
| `00_Overview.md` | Architecture summary and file index |
| `reference_mcp_server/*.cs.bak` | Reference MCP server implementation (backup) |
| `01_*.cs.txt` - `05_*.cs.txt` | New source file proposals |
| `06_*.cs.txt` - `10_*.cs.txt` | Modifications to existing files |
| `08_*.txt` | Policy file modifications |

## When Working in This Repository

If this repository is the active workspace:

1. **Read the overview** (`00_Overview.md`) to understand the proposal
2. **Check existing implementations** - many services referenced in the proposal may already exist in `CodexAppServerBlazor.AICodingServices/Workflow/`
3. **Verify tool availability** - search for MCP tool wrappers before creating new ones
4. **Generate tests** - if implementing any proposal items, write tests first

## Test Generation Requirements

Any implementation from this proposal MUST include:

- [ ] Unit tests for new service classes
- [ ] Integration tests for new MCP tool wrappers
- [ ] Workflow state machine tests (phase transitions)
- [ ] File capability analyzer tests
- [ ] Merge review state tests

## Finding Existing Implementations

Before creating new implementations, search for existing code:

```bash
# Search for workflow services
grep -r "WorkflowEditService\|RoslynEditService" --include="*.cs"

# Search for existing MCP tools
grep -r "McpServerTool" --include="*.cs"

# Search for session state
grep -r "WorkflowSessionState" --include="*.cs"
```

## Verification Checklist

Before marking any proposal item as implemented:

- [ ] Backend service exists in `CodexAppServerBlazor.AICodingServices/`
- [ ] MCP tool wrapper is properly exposed
- [ ] Existing tests pass
- [ ] New tests are written and passing
- [ ] DI registration is added to `Program.cs`
- [ ] No duplicate implementations

## References

- Original proposal: `docs/OpenHands_ProposedPlan/00_Overview.md`
- Reference MCP server: `docs/OpenHands_ProposedPlan/reference_mcp_server/`
