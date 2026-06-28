namespace CodexAppServerBlazor.AICodingServices.Indexing;

public static class PostAcceptIndexRefreshTelemetry
{
    public static bool ShouldLogPhase(IReadOnlyDictionary<string, string> properties)
    {
        return !(properties.TryGetValue("projectPath", out string? projectPath)
            && !string.IsNullOrWhiteSpace(projectPath));
    }
}
