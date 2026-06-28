using System.Security.Cryptography;
using System.Text;

namespace CodexAppServerBlazor.AICodingServices.Logging;

public static class McpTrafficLogPolicy
{
    private const int PreviewLimit = 500;
    private const int StoredPayloadLimit = 2048;

    public static bool ShouldLogTraffic(string? method, string? toolName)
    {
        if (string.Equals(method, "initialize", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "tools/list", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "resources/list", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "resources/templates/list", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static void AddPayloadProperties(
        IDictionary<string, string> properties,
        string key,
        string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        properties[key + "Preview"] = Truncate(text, PreviewLimit);
        properties[key + "Length"] = text.Length.ToString();
        properties[key + "Sha256"] = ComputeSha256(text);

        if (text.Length <= StoredPayloadLimit)
        {
            properties[key] = text;
        }
        else
        {
            properties[key + "Truncated"] = "true";
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private static string ComputeSha256(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }
}
