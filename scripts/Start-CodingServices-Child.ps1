param(
    [string]$WorkspaceRoot = "C:\SchemaStudioWebViewer1",
    [string]$WatchedSolutionPath = "C:\SchemaStudioWebViewer1\SchemaStudioWebViewer.sln"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$projectPath = Join-Path $repoRoot "CodexAppServerBlazor\CodexAppServerBlazor.csproj"

dotnet run --project $projectPath -- `
  --BlazorHost:Url=http://localhost:5215 `
  --Mcp:Url=http://localhost:6289 `
  --Workspace:DefaultCwd=$WorkspaceRoot `
  --Workspace:PersistencePath=runtime/app-state/selected-workspace-child.txt `
  --CodingServices:RuntimeRoot="$repoRoot\runtime" `
  --CodingServices:WatchedSolutionPath=$WatchedSolutionPath
