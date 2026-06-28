namespace CodexAppServerBlazor.AICodingServices.Core;

public sealed record CodingServicesSettings(
    string RepositoryRoot,
    string RuntimeRoot,
    string WatchedSolutionPath,
    IReadOnlyList<string> TestProjectPaths,
    IReadOnlyList<string> WinMergeCandidatePaths,
    string DefaultReviewSurface,
    string BrowserReviewBaseUrl)
{
    public string WatchedProjectFolder =>
        Path.GetDirectoryName(WatchedSolutionPath) ?? string.Empty;

    public static CodingServicesSettings Create(
        string repositoryRoot,
        string watchedSolutionPath,
        string? runtimeRoot = null,
        IReadOnlyList<string>? testProjectPaths = null,
        IReadOnlyList<string>? winMergeCandidatePaths = null,
        string? defaultReviewSurface = null,
        string? browserReviewBaseUrl = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedWatchedSolutionPath = Path.GetFullPath(watchedSolutionPath);
        string resolvedRuntimeRoot = Path.GetFullPath(
            runtimeRoot ?? Path.Combine(resolvedRepositoryRoot, "runtime"));

        return new CodingServicesSettings(
            resolvedRepositoryRoot,
            resolvedRuntimeRoot,
            resolvedWatchedSolutionPath,
            testProjectPaths?.Select(Path.GetFullPath).ToArray() ?? [],
            winMergeCandidatePaths ?? [],
            string.IsNullOrWhiteSpace(defaultReviewSurface) ? "Browser" : defaultReviewSurface,
            string.IsNullOrWhiteSpace(browserReviewBaseUrl) ? "http://localhost:5000" : browserReviewBaseUrl.TrimEnd('/'));
    }
}
