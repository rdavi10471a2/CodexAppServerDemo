using System.Security.Cryptography;
using System.Text;
using CodexAppServerBlazor.AICodingServices.Core;

namespace CodexAppServerBlazor.AICodingServices.Logging;

public static class MonitorLogPipeNames
{
    public static string GetDefaultPipeName(CodingServicesSettings settings)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(settings.RuntimeRoot));
        string hash = Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
        return $"CodexAppServerBlazor.AICodingServices.Log.{hash}";
    }
}
