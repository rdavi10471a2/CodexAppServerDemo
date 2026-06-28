namespace CodexAppServerBlazor.AICodingServices.Core;

public sealed record WatchedSolutionInfo(
    string SolutionPath,
    string ProjectFolder,
    bool SolutionExists)
{
    public static WatchedSolutionInfo FromSettings(CodingServicesSettings settings)
    {
        return new WatchedSolutionInfo(
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            File.Exists(settings.WatchedSolutionPath));
    }
}
