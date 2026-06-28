using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAppServerBlazor.AICodingServices.Core;

public static class CodingServicesSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    static CodingServicesSettingsLoader()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static CodingServicesSettings Load(string repositoryRoot, string? settingsPath = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = ResolveSettingsPath(resolvedRepositoryRoot, settingsPath);

        if (!File.Exists(resolvedSettingsPath))
        {
            throw new FileNotFoundException("AICodingServices settings file was not found.", resolvedSettingsPath);
        }

        using (FileStream stream = File.OpenRead(resolvedSettingsPath))
        {
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                JsonElement codingServices = document.RootElement.GetProperty("CodingServices");
                string watchedSolutionPath = RequireString(codingServices, "WatchedSolutionPath");
                string runtimeRoot = GetString(codingServices, "RuntimeRoot") ?? "runtime";
                string settingsDirectory = Path.GetDirectoryName(resolvedSettingsPath) ?? resolvedRepositoryRoot;
                IReadOnlyList<string> winMergeCandidatePaths = LoadWinMergeCandidatePaths(
                    codingServices,
                    resolvedRepositoryRoot,
                    settingsDirectory);
                string defaultReviewSurface = GetString(codingServices, "DefaultReviewSurface") ?? "Browser";
                string browserReviewBaseUrl = GetString(codingServices, "BrowserReviewBaseUrl") ?? "http://localhost:5000";

                return CodingServicesSettings.Create(
                    resolvedRepositoryRoot,
                    ResolvePath(watchedSolutionPath, settingsDirectory),
                    ResolvePath(runtimeRoot, resolvedRepositoryRoot),
                    ResolvePaths(winMergeCandidatePaths, settingsDirectory),
                    defaultReviewSurface,
                    browserReviewBaseUrl);
            }
        }
    }

    public static string SaveLocal(
        string repositoryRoot,
        string watchedSolutionPath,
        string? runtimeRoot = null,
        string? settingsPath = null)
    {
        string resolvedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        string resolvedSettingsPath = ResolveSettingsPath(resolvedRepositoryRoot, settingsPath);
        string resolvedWatchedSolutionPath = Path.GetFullPath(watchedSolutionPath);
        string resolvedRuntimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? "runtime"
            : runtimeRoot;

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedSettingsPath) ?? resolvedRepositoryRoot);
        IReadOnlyList<string> existingWinMergeCandidatePaths = LoadExistingWinMergeCandidatePaths(
            resolvedSettingsPath,
            resolvedRepositoryRoot);
        string existingDefaultReviewSurface = LoadExistingString(
            resolvedSettingsPath,
            "DefaultReviewSurface",
            "Browser");
        string existingBrowserReviewBaseUrl = LoadExistingString(
            resolvedSettingsPath,
            "BrowserReviewBaseUrl",
            "http://localhost:5000");
        LocalSettingsFile file = new(
            new LocalCodingServicesSettings(
                resolvedWatchedSolutionPath,
                resolvedRuntimeRoot,
                existingWinMergeCandidatePaths,
                existingDefaultReviewSurface,
                existingBrowserReviewBaseUrl));
        File.WriteAllText(resolvedSettingsPath, JsonSerializer.Serialize(file, SerializerOptions) + Environment.NewLine);
        return resolvedSettingsPath;
    }

    private static string ResolveSettingsPath(string resolvedRepositoryRoot, string? settingsPath)
    {
        return Path.GetFullPath(
            settingsPath ?? Path.Combine(resolvedRepositoryRoot, "config", "appsettings.json"));
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        string? value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"CodingServices:{propertyName} is required.");
        }

        return value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetStringArray(JsonElement element, string propertyName, out IReadOnlyList<string> values)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            values = [];
            return false;
        }

        List<string> items = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                items.Add(item.GetString()!);
            }
        }

        values = items;
        return true;
    }

    private static IReadOnlyList<string> LoadWinMergeCandidatePaths(
        JsonElement monitor,
        string resolvedRepositoryRoot,
        string settingsDirectory)
    {
        if (TryGetStringArray(monitor, "WinMergeCandidatePaths", out IReadOnlyList<string> configuredPaths))
        {
            return ResolvePaths(configuredPaths, settingsDirectory);
        }

        return LoadTemplateWinMergeCandidatePaths(resolvedRepositoryRoot);
    }

    private static IReadOnlyList<string> LoadExistingWinMergeCandidatePaths(
        string settingsPath,
        string resolvedRepositoryRoot)
    {
        if (!File.Exists(settingsPath))
        {
            return LoadTemplateWinMergeCandidatePaths(resolvedRepositoryRoot);
        }

        using (FileStream stream = File.OpenRead(settingsPath))
        {
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                if (!document.RootElement.TryGetProperty("CodingServices", out JsonElement codingServices))
                {
                    return LoadTemplateWinMergeCandidatePaths(resolvedRepositoryRoot);
                }

                return TryGetStringArray(codingServices, "WinMergeCandidatePaths", out IReadOnlyList<string> existingPaths)
                    ? existingPaths
                    : LoadTemplateWinMergeCandidatePaths(resolvedRepositoryRoot);
            }
        }
    }

    private static string LoadExistingString(string settingsPath, string propertyName, string defaultValue)
    {
        if (!File.Exists(settingsPath))
        {
            return defaultValue;
        }

        using (FileStream stream = File.OpenRead(settingsPath))
        {
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                if (!document.RootElement.TryGetProperty("CodingServices", out JsonElement codingServices))
                {
                    return defaultValue;
                }

                return GetString(codingServices, propertyName) ?? defaultValue;
            }
        }
    }

    private static IReadOnlyList<string> LoadTemplateWinMergeCandidatePaths(string resolvedRepositoryRoot)
    {
        string templatePath = Path.Combine(resolvedRepositoryRoot, "config", "appsettings.template.json");
        if (!File.Exists(templatePath))
        {
            return [];
        }

        using (FileStream stream = File.OpenRead(templatePath))
        {
            using (JsonDocument document = JsonDocument.Parse(stream))
            {
                if (!document.RootElement.TryGetProperty("CodingServices", out JsonElement codingServices)
                    || !TryGetStringArray(codingServices, "WinMergeCandidatePaths", out IReadOnlyList<string> templatePaths))
                {
                    return [];
                }

                string templateDirectory = Path.GetDirectoryName(templatePath) ?? resolvedRepositoryRoot;
                return ResolvePaths(templatePaths, templateDirectory);
            }
        }
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static IReadOnlyList<string> ResolvePaths(IReadOnlyList<string> paths, string baseDirectory)
    {
        return paths
            .Select(path => ResolvePath(path, baseDirectory))
            .ToArray();
    }

    private sealed record LocalSettingsFile(LocalCodingServicesSettings CodingServices);

    private sealed record LocalCodingServicesSettings(
        string WatchedSolutionPath,
        string RuntimeRoot,
        IReadOnlyList<string> WinMergeCandidatePaths,
        string DefaultReviewSurface,
        string BrowserReviewBaseUrl);
}
