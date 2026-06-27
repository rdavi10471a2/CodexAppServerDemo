# Codex App Server WinForms Sample - MCP file boundary

This sample is a WinForms cockpit for `codex app-server` using the default stdio JSONL transport.

The important correction in this version is that the selected file is no longer inlined into the prompt by the WinForms client. The app starts a local MCP HTTP server in the same process and exposes the selected file through a controlled tool boundary.

## Boundaries

```text
WinForms harness
  - owns UI state
  - starts/stops codex app-server
  - starts/stops local MCP host
  - records selected repo/file
  - does not inline selected file content into prompts

Local MCP host
  - listens at http://localhost:6278
  - exposes FileMcpTools.GetSelectedFile
  - calls HarnessFileWorkflow
  - returns metadata/content for only the selected file

Codex app-server
  - owns model turns and telemetry
  - should fetch selected file through the harness MCP tool
```

## MCP endpoint

The embedded MCP server listens here:

```text
http://localhost:6278
```

Tool exposed:

```text
GetSelectedFile(includeContent: bool = true)
```

The tool returns:

```text
Success
RepoRoot
FilePath
RelativePath
FileName
Length
Content
Error
```

## Run

```powershell
cd CodexAppServerWinForms
dotnet restore
dotnet run
```

Then:

1. Click **Start Server**.
2. Pick a repo root.
3. Pick a file.
4. Click **Start Thread**.
5. Click **Send Turn**.

The generated turn tells Codex to fetch the selected file through `GetSelectedFile` instead of relying on direct prompt-injected file content.

## Files added/restored

```text
Mcp/SelectedFileState.cs
Mcp/HarnessFileWorkflow.cs
Mcp/FileMcpTools.cs
Mcp/McpHostFactory.cs
```

`Program.cs` now starts the MCP host before showing `MainForm` and stops it on exit.

`MainForm.cs` now updates `SelectedFileState` when the repo/file are selected and sends only metadata plus MCP instructions in the turn.

`CodexAppServerWinForms.csproj` now uses `Microsoft.NET.Sdk.Web` and references `ModelContextProtocol.AspNetCore`.

## Note

This restores the harness-side MCP boundary. Codex still needs to be able to discover/connect to the local MCP server through whatever MCP configuration path your installed Codex app-server build expects. The app now hosts the server and no longer bypasses it by shoving file content directly into the prompt.
