using AICodingServices.Core;
using AICodingServices.Data;
using AICodingServices.Indexing;
using AICodingServices.Logging;
using AICodingServices.Runtime;
using AICodingServices.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AICodingServices.McpServer;

public sealed class AICodingServicesMcpRuntimeState
{
    private readonly IMonitorLogger logger;
    private long lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
    private int shutdownRequested;

    public AICodingServicesMcpRuntimeState(IMonitorLogger logger)
    {
        this.logger = logger;
    }

    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref lastActivityTicks), TimeSpan.Zero);

    public bool ShutdownRequested => Volatile.Read(ref shutdownRequested) == 1;

    public void Touch([CallerMemberName] string toolName = "")
    {
        Interlocked.Exchange(ref lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
        logger.Write(
            MonitorLogLevel.Information,
            "AICodingServices.McpServer",
            "adapter.mcp.tool.called",
            "MCP tool call observed.",
            new Dictionary<string, string>
            {
                ["requestId"] = Guid.NewGuid().ToString("N"),
                ["adapterProtocol"] = "mcp",
                ["toolName"] = ToSnakeCase(toolName),
                ["memberName"] = toolName,
                ["isError"] = "false"
            });
    }

    public void RequestShutdown(string? reason)
    {
        _ = reason;
        Volatile.Write(ref shutdownRequested, 1);
        Touch();
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
