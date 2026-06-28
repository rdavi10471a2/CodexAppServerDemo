using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.AICodingServices.Logging;

public static class MonitorLogPaths
{
    public static string GetDefaultLogPath(CodingServicesSettings settings)
    {
        return Path.Combine(settings.RuntimeRoot, "logs", "aimonitor.ndjson");
    }
}
