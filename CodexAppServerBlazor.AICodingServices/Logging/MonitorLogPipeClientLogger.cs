using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAppServerBlazor.AICodingServices.Logging;

public sealed class MonitorLogPipeClientLogger : IMonitorLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string pipeName;
    private readonly IMonitorLogger fallbackLogger;
    private readonly TimeSpan connectTimeout;
    private readonly object fallbackStateGate = new();
    private DateTimeOffset lastUnavailableNoticeUtc = DateTimeOffset.MinValue;

    static MonitorLogPipeClientLogger()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public MonitorLogPipeClientLogger(
        string pipeName,
        IMonitorLogger fallbackLogger,
        TimeSpan? connectTimeout = null)
    {
        this.pipeName = pipeName;
        this.fallbackLogger = fallbackLogger;
        this.connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(200);
    }

    public void Write(
        MonitorLogLevel level,
        string source,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        MonitorLogEntry entry = new(
            DateTimeOffset.UtcNow,
            level,
            source,
            eventName,
            message,
            properties ?? new Dictionary<string, string>());

        string line = JsonSerializer.Serialize(entry, SerializerOptions);
        try
        {
            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out, PipeOptions.WriteThrough);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            using StreamWriter writer = new(pipe)
            {
                AutoFlush = true
            };
            writer.WriteLine(line);
        }
        catch (IOException)
        {
            WriteFallback(level, source, eventName, message, properties);
        }
        catch (TimeoutException)
        {
            WriteFallback(level, source, eventName, message, properties);
        }
    }

    private void WriteFallback(
        MonitorLogLevel level,
        string source,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? properties)
    {
        Dictionary<string, string> fallbackProperties = properties is null
            ? []
            : new Dictionary<string, string>(properties);
        fallbackProperties["hubRunning"] = "false";
        fallbackProperties["logDelivery"] = "direct-file-fallback";
        fallbackProperties["pipeName"] = pipeName;
        bool shouldWriteUnavailable;
        lock (fallbackStateGate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            shouldWriteUnavailable = now - lastUnavailableNoticeUtc >= TimeSpan.FromSeconds(30);
            if (shouldWriteUnavailable)
            {
                lastUnavailableNoticeUtc = now;
            }
        }

        if (shouldWriteUnavailable)
        {
            fallbackLogger.Write(
                MonitorLogLevel.Warning,
                source,
                "adapter.hub.unavailable",
                "WinForms hub log pipe was unavailable; adapter wrote directly to the runtime log.",
                fallbackProperties);
        }

        fallbackLogger.Write(level, source, eventName, message, fallbackProperties);
    }
}
