using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.AICodingServices.MSBuild;
using CodexAppServerBlazor.AICodingServices.Workflow;

namespace CodexAppServerBlazor.AICodingServices.Indexing;

public sealed class SolutionIndexRebuildService
{
    public async Task<SolutionIndexSummary> RebuildAsync(
        CodingServicesSettings settings,
        CancellationToken cancellationToken = default,
        Action<string, long, IReadOnlyDictionary<string, string>>? timingSink = null)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(store);
        SolutionIndexSummary summary = await builder.RebuildAsync(settings, cancellationToken, timingSink);
        new WorkflowEditService(settings).MarkAllIndexesFresh();
        return summary;
    }

    public async Task<SolutionIndexSummary> RefreshProjectFilesAsync(
        CodingServicesSettings settings,
        string projectPath,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default,
        Action<string, long, IReadOnlyDictionary<string, string>>? timingSink = null)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(store);
        return await builder.RefreshProjectFilesAsync(settings, projectPath, filePaths, cancellationToken, timingSink);
    }
}
