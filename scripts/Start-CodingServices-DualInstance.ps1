param(
    [string]$ChildWorkspaceRoot = "C:\SchemaStudioWebViewer1",
    [string]$ChildWatchedSolutionPath = "C:\SchemaStudioWebViewer1\SchemaStudioWebViewer.sln"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Start-Process powershell.exe -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Start-CodingServices-SelfHost.ps1")
) -WorkingDirectory $repoRoot

Start-Process powershell.exe -ArgumentList @(
    "-NoExit",
    "-ExecutionPolicy", "Bypass",
    "-File", (Join-Path $PSScriptRoot "Start-CodingServices-Child.ps1"),
    "-WorkspaceRoot", $ChildWorkspaceRoot,
    "-WatchedSolutionPath", $ChildWatchedSolutionPath
) -WorkingDirectory $repoRoot
