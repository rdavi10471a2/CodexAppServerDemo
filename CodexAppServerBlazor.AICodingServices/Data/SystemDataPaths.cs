using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.AICodingServices.Data;

public static class SystemDataPaths
{
    public static string GetDefaultIndexDatabasePath(CodingServicesSettings settings)
    {
        return Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "data",
            "solution-index.sqlite");
    }

    public static string GetDefaultPlanningDatabasePath(CodingServicesSettings settings)
    {
        return Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "board.sqlite");
    }

    public static string GetDefaultTaskMemoryRoot(CodingServicesSettings settings)
    {
        return Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "task-memory");
    }
}
