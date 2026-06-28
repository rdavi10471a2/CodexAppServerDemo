using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.AICodingServices.Data;

public static class MonitorDataPaths
{
    public static string GetDefaultIndexDatabasePath(CodingServicesSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "data",
            "solution-index.sqlite");
    }

    public static string GetDefaultPlanningDatabasePath(CodingServicesSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "board.sqlite");
    }

    public static string GetDefaultTaskMemoryRoot(CodingServicesSettings settings)
    {
        return Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "task-memory");
    }
}
