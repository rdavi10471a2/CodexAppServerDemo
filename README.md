# Codex App Server Blazor Control

This repository contains a Blazor Server control surface for `codex app-server`.

The old WinForms harness has been removed. The app is now centered on a CWD/workspace workflow instead of a selected-file workflow.

## What It Does

- Starts and talks to `codex app-server`.
- Lets the user set the Codex current working directory.
- Shows the prompt and assistant response together on the Assistant tab.
- Keeps status, telemetry, tool activity, and raw protocol output in separate tabs.
- Hosts the local MCP HTTP endpoint for compatibility while the workflow is ported forward.

## Run

```powershell
dotnet restore .\CodexAppServerWinForms_corrected.slnx
dotnet build .\CodexAppServerWinForms_corrected.slnx
dotnet run --project .\CodexAppServerBlazor\CodexAppServerBlazor.csproj --urls http://localhost:5205
```

Open:

```text
http://localhost:5205/
```

MCP health:

```text
http://localhost:6278/health
```

## Project Layout

```text
CodexAppServerBlazor/       Blazor UI and app host
CodexAppServerClient.cs     codex app-server JSON-RPC client
Mcp/                        MCP HTTP host and compatibility tools
CodexEvents.cs              protocol event models
JsonRpcMessage.cs           JSON-RPC message model
```

## Workflow Direction

The intended workflow is:

```text
choose CWD -> discovery -> proposal -> edit/diff -> compile -> reindex
```

The UI should stay focused on that order. Avoid rebuilding selected-file assumptions into the prompt path.
