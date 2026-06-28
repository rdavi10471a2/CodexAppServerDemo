using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAppServerBlazor.AICodingServices.Logging;

public sealed class MonitorLogPipeServer : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string pipeName;
    private readonly IMonitorLogger sink;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listenerTask;

    static MonitorLogPipeServer()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public MonitorLogPipeServer(string pipeName, IMonitorLogger sink)
    {
        this.pipeName = pipeName;
        this.sink = sink;
    }

    public string PipeName => pipeName;

    public void Start()
    {
        listenerTask ??= Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            listenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        cancellation.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream pipe = new(
                    pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using StreamReader reader = new(pipe);
                while (!cancellation.IsCancellationRequested && await reader.ReadLineAsync(cancellation.Token) is { } line)
                {
                    WriteLineToSink(line);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private void WriteLineToSink(string line)
    {
        try
        {
            MonitorLogEntry? entry = JsonSerializer.Deserialize<MonitorLogEntry>(line, SerializerOptions);
            if (entry is not null)
            {
                sink.Write(entry.Level, entry.Source, entry.EventName, entry.Message, entry.Properties);
            }
        }
        catch (JsonException)
        {
        }
    }
}
