# Codex App Server Blazor Control

This repository contains a Blazor Server control surface for `codex app-server`.

The old WinForms harness has been removed. The app is now centered on a CWD/workspace workflow instead of a selected-file workflow.

## What It Does

- Starts and talks to `codex app-server`.
- Lets the user set the Codex current working directory.
- Shows the prompt and assistant response together on the Assistant tab.
- Keeps status, telemetry, tool activity, and raw protocol output in separate tabs.
- Hosts the local MCP HTTP endpoint for workspace metadata.

## Run

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj
```

The default app and MCP ports are configured in
`CodexAppServerBlazor/appsettings.json`:

```json
"BlazorHost": {
  "Url": "http://localhost:5205"
},
"Mcp": {
  "Url": "http://localhost:6278"
}
```

Open:

```text
http://localhost:5205/
```

MCP health:

```text
http://localhost:6278/health
```

The health response advertises the local MCP discovery surface. It includes the
endpoint metadata plus tool names and descriptions:

- `GetWorkspace`: returns the current workspace CWD selected in the Blazor UI.
- `GetWatchedSolutionDigest`: returns cheap readiness and change-detection
  metadata for the watched solution, including counts, summary size, hash, and
  index paths.
- `GetWatchedSolutionSummary`: returns the full indexed project/file/type/member
  tree for on-demand discovery. It does not include source file bodies.
- `GetTestProjectSummary`: returns the indexed project/file/type/member tree for
  configured test projects only. It does not include source file bodies.

The Source tab and startup context use the product-source projection by default.
Configured test projects stay indexed and editable, but are shown separately in
the Tests tab and through `GetTestProjectSummary`.

## Project Layout

```text
CodexAppServerBlazor/       Blazor UI and app host
CodexAppServerClient.cs     codex app-server JSON-RPC client
Mcp/                        MCP HTTP host and workspace metadata tool
CodexEvents.cs              protocol event models
JsonRpcMessage.cs           JSON-RPC message model
```

## Workflow Direction

The intended workflow is:

```text
choose CWD -> discovery -> proposal -> edit/diff -> compile -> reindex
```

The UI should stay focused on that order. Avoid rebuilding selected-file assumptions into the prompt path.
