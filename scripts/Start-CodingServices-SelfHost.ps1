$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$projectPath = Join-Path $repoRoot "CodexAppServerBlazor\CodexAppServerBlazor.csproj"
$watchedSolutionPath = Join-Path $repoRoot "CodexAppServerWinForms_corrected.slnx"

dotnet run --project $projectPath -- `
  --BlazorHost:Url=http://localhost:5205 `
  --Mcp:Url=http://localhost:6278 `
  --Workspace:DefaultCwd=$repoRoot `
  --Workspace:PersistencePath=runtime/app-state/selected-workspace-selfhost.txt `
  --CodingServices:RuntimeRoot="$repoRoot\runtime" `
  --CodingServices:WatchedSolutionPath=$watchedSolutionPath
