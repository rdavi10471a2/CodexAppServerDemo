using System.Globalization;
using System.Text;

namespace CodexAppServerBlazor.AICodingServices.Workflow;

public sealed class GovernedCommandArtifactWriter
{
    private readonly string toolLogRoot;

    public GovernedCommandArtifactWriter(string runtimeRoot)
    {
        toolLogRoot = Path.Combine(runtimeRoot, "tool-logs");
    }

    public string WriteArtifact(
        GovernedCommandRequest request,
        GovernedCommandRawResult rawResult,
        GovernedCommandKind kind,
        DateTimeOffset? timestampUtc = null)
    {
        DateTimeOffset timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        Directory.CreateDirectory(toolLogRoot);
        string fileName = timestamp.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture)
            + "-"
            + Sanitize(kind.ToString())
            + "-"
            + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8]
            + ".log";
        string path = Path.Combine(toolLogRoot, fileName);

        StringBuilder builder = new();
        builder.AppendLine("# Governed command full output");
        builder.AppendLine("timestampUtc: " + timestamp.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("kind: " + kind);
        builder.AppendLine("exitCode: " + rawResult.ExitCode.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("durationMs: " + ((long)rawResult.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("workingDirectory: " + (request.WorkingDirectory ?? string.Empty));
        builder.AppendLine("command: " + request.Command);
        builder.AppendLine();
        builder.AppendLine("## stdout");
        builder.AppendLine(rawResult.StandardOutput);
        builder.AppendLine();
        builder.AppendLine("## stderr");
        builder.AppendLine(rawResult.StandardError);

        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}
