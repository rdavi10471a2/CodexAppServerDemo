using System.Diagnostics;

namespace CodexAppServerBlazor.Services;

public sealed class ExternalBrowserLaunchService
{
    private static readonly string[] ChromeCandidatePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
    ];

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("A browser URL is required.", nameof(url));
        }

        string? chromePath = ChromeCandidatePaths.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(chromePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = BuildChromeArguments(url),
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string BuildChromeArguments(string url)
    {
        // Reuse the running Chrome session when possible so governed review stays
        // in a single browser window instead of spraying new standalone windows.
        return $"--new-tab \"{url}\"";
    }
}
