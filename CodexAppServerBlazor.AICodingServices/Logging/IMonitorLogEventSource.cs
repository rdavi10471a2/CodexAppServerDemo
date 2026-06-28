namespace CodexAppServerBlazor.AICodingServices.Logging;

public interface IMonitorLogEventSource
{
    event Action<MonitorLogEntry>? EntryWritten;
}
