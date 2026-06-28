using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Services;

public sealed class WorkspaceStartupHostedService : BackgroundService
{
    private readonly IConfiguration configuration;
    private readonly WorkspaceState workspaceState;
    private readonly SourceWorkspaceService sourceWorkspaceService;
    private readonly CodexConnectionService connectionService;

    public WorkspaceStartupHostedService(
        IConfiguration configuration,
        WorkspaceState workspaceState,
        SourceWorkspaceService sourceWorkspaceService,
        CodexConnectionService connectionService)
    {
        this.configuration = configuration;
        this.workspaceState = workspaceState;
        this.sourceWorkspaceService = sourceWorkspaceService;
        this.connectionService = connectionService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string cwd = configuration["Workspace:DefaultCwd"] ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(cwd))
        {
            connectionService.ReportStatus(
                "WorkspaceStartup",
                "error",
                "coding-services",
                $"Configured CWD does not exist: {cwd}");
            return;
        }

        try
        {
            bool workspaceIsValid = await ValidateOrRebuildAsync(cwd, stoppingToken);
            if (workspaceIsValid)
            {
                workspaceState.SetRepoRoot(cwd);
                connectionService.ReportStatus(
                    "WorkspaceStartup",
                    "ok",
                    "coding-services",
                    $"Selected startup workspace for MCP: {Path.GetFullPath(cwd)}");
                await AutoStartCodexServerAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            connectionService.ReportStatus(
                "WorkspaceStartup",
                "error",
                "coding-services",
                $"Startup workspace check failed: {ex.Message}");
        }
    }

    private async Task<bool> ValidateOrRebuildAsync(string cwd, CancellationToken cancellationToken)
    {
        bool forceRebuild = configuration.GetValue("CodingServices:RebuildIndexOnStartup", false);
        bool rebuildWhenMissingOrStale = configuration.GetValue("CodingServices:RebuildIndexWhenMissingOrStaleOnStartup", false);
        SourceWorkspaceStructureSnapshot snapshot = sourceWorkspaceService.BuildStructureSnapshot(cwd, filter: null);
        bool indexReady = File.Exists(snapshot.IndexDatabasePath)
            && snapshot.FileCount > 0
            && snapshot.Tree.Count > 0
            && string.IsNullOrWhiteSpace(snapshot.Message);

        if (forceRebuild || (rebuildWhenMissingOrStale && !indexReady))
        {
            string reason = forceRebuild ? "forced by configuration" : "missing, empty, or stale";
            connectionService.ReportStatus(
                "WorkspaceStartup",
                "running",
                "coding-services",
                $"Rebuilding watched solution index on startup because it is {reason}.");

            await sourceWorkspaceService.RebuildIndexAsync(cwd, cancellationToken);
            snapshot = sourceWorkspaceService.BuildStructureSnapshot(cwd, filter: null);
            indexReady = File.Exists(snapshot.IndexDatabasePath)
                && snapshot.FileCount > 0
                && snapshot.Tree.Count > 0
                && string.IsNullOrWhiteSpace(snapshot.Message);
        }

        if (!File.Exists(snapshot.WatchedSolutionPath))
        {
            connectionService.ReportStatus(
                "WorkspaceStartup",
                "error",
                "coding-services",
                $"No valid watched solution found for CWD: {cwd}");
            return false;
        }

        string status = indexReady ? "ok" : "skipped";
        string detail = indexReady
            ? $"Workspace context ready: {snapshot.Tree.Count} projects, {snapshot.FileCount} indexed files."
            : $"Workspace context not ready: {snapshot.Message}";
        connectionService.ReportStatus("WorkspaceStartup", status, "coding-services", detail);
        return true;
    }

    private async Task AutoStartCodexServerAsync(CancellationToken cancellationToken)
    {
        bool autoStart = configuration.GetValue("CodexAppServer:AutoStartOnStartup", true);
        if (!autoStart)
        {
            connectionService.ReportStatus(
                "CodexStartup",
                "skipped",
                "coding-services",
                "Automatic codex app-server startup is disabled.");
            return;
        }

        string codexExecutable = configuration["CodexAppServer:Executable"] ?? "codex";
        try
        {
            await connectionService.StartServerAsync(codexExecutable, cancellationToken);
            connectionService.ReportStatus(
                "CodexStartup",
                "ok",
                "coding-services",
                $"Started codex app-server automatically using '{codexExecutable}'.");
        }
        catch (InvalidOperationException ex)
        {
            connectionService.ReportStatus(
                "CodexStartup",
                "skipped",
                "coding-services",
                $"Could not auto-start codex app-server yet: {ex.Message}");
        }
        catch (Exception ex)
        {
            connectionService.ReportStatus(
                "CodexStartup",
                "error",
                "coding-services",
                $"Automatic codex app-server startup failed: {ex.Message}");
        }
    }
}
