# AGENTS.md

## Purpose

This repository is a WinForms harness for `codex app-server` with a local MCP HTTP host.

The key design rule is the file-boundary model:

- The WinForms app may provide selected file metadata in the prompt.
- Selected file content must be fetched through the local MCP tool boundary.
- Do not inline selected file contents into generated turn text.

## Working Agreements

- Keep changes small and explicit.
- Preserve the harness/MCP separation.
- Prefer fixes that make context flow observable in the UI or logs.
- Do not add hidden prompt injection paths through the WinForms client.

## MCP Boundary

- Local MCP endpoint: `http://localhost:6278`
- Tool: `GetSelectedFile(includeContent: bool = true)`
- When discussing the selected file, fetch it through the MCP tool rather than assuming prompt-injected content.
- Do not broaden file access unless the user explicitly asks for wider repo inspection.

## Repo Map

- `MainForm.cs`: WinForms UI, prompt assembly, turn submission, and event display.
- `CodexAppServerClient.cs`: JSON-RPC client for `codex app-server`, protocol events, and token usage handling.
- `Mcp/`: local MCP host, selected-file state, and file tool workflow.
- `Program.cs`: app startup and MCP host lifetime.

## Build And Run

Use these commands from the repository root:

```powershell
dotnet restore
dotnet build
dotnet run
```

## Startup Check

For a basic manual smoke test:

1. Start the app.
2. Click `Start Server`.
3. Select a repo root and a file.
4. Start a thread and send a turn.
5. Confirm the prompt includes only metadata for the selected file.
6. Confirm Codex fetches file content through `GetSelectedFile`.

## When Editing

- If you change turn construction, verify the selected file content is still not embedded in the prompt.
- If you change telemetry or protocol handling, keep raw protocol visibility intact.
- If you add instruction discovery or logging features, make the source of that context visible to the user.
