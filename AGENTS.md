# AGENTS.md

## Purpose

This repository is now a Blazor control surface for `codex app-server`.

The primary workflow is CWD/workspace based:

- The user chooses a current working directory in the Blazor UI.
- Codex turns should treat that CWD as the loaded workspace.
- Do not assume a selected-file workflow.
- Do not ask Codex to fetch source through selected-file tools unless a future task explicitly reintroduces that workflow.

## Working Agreements

- Keep changes small and explicit.
- Prefer workflow order: discovery, proposal, edit/diff, compile, reindex.
- Make context sources visible in the UI or logs.
- Keep UI, MCP, and Codex app-server connection code separated by service boundaries.

## Repo Map

- `CodexAppServerBlazor/`: Blazor Server control UI and app host.
- `CodexAppServerClient.cs`: JSON-RPC client for `codex app-server`, protocol events, and token usage handling.
- `Mcp/`: local MCP HTTP host and workspace metadata tools. The health endpoint
  under configured `Mcp:Url` advertises available MCP discovery tools and
  descriptions.
- `CodexAppServerWinForms_corrected.slnx`: root solution pointing at the Blazor project.

## Build And Run

Use these commands from the repository root:

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj
```

Default ports are configured in `CodexAppServerBlazor/appsettings.json`:
`BlazorHost:Url` controls the Blazor app URL and `Mcp:Url` controls the local
MCP endpoint URL.

Configured `CodingServices:TestProjectPaths` are part of the watched solution
and index. The Source tab and initial bootstrap context use the product-source
projection by default; the Tests tab and `GetTestProjectSummary` expose test
structure explicitly when it is relevant.

## Startup Check

For a basic manual smoke test:

1. Start the Blazor app.
2. Choose or type the CWD.
3. Click `Start Server`.
4. Click `Start Thread`.
5. Send a turn from the Assistant tab.
6. Confirm the prompt and assistant response remain visible together.

## When Editing

- If you change turn construction, verify it remains CWD/workspace based.
- If you change telemetry or protocol handling, keep raw protocol visibility intact.
- If you change the UI, build and restart the Blazor app before reporting it is visible.
