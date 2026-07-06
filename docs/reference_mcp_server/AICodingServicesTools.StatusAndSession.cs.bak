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

public sealed partial class AICodingServicesTools
{
    [McpServerTool]
    [Description("Return paths and high-level status for the AICodingServices MCP server and watched solution.")]
    public AICodingServicesMcpStatus GetMonitorStatus()
    {
        runtimeState.Touch();
        MonitorStatusResult indexStatus = queryService.GetMonitorStatus();
        return new AICodingServicesMcpStatus(
            settings.RepositoryRoot,
            settings.RuntimeRoot,
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            indexStatus.DatabasePath,
            indexStatus.DatabaseExists,
            indexStatus.ProjectCount,
            indexStatus.DocumentCount,
            indexStatus.SymbolCount,
            indexStatus.ReferenceCount,
            indexStatus.CallSiteCount,
            indexStatus.RelationshipCount,
            indexStatus.StaleFileCount,
            indexStatus.DiagnosticCount);
    }

    [McpServerTool]
    [Description("Return the monitor workflow status, including watched solution, runtime root, Working folder, and configured WinMerge candidates.")]
    public AICodingServicesWorkflowStatus GetWorkflowStatus()
    {
        runtimeState.Touch();
        return new AICodingServicesWorkflowStatus(
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            settings.RuntimeRoot,
            workflowPaths.WorkingRoot,
            settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists),
            settings.WinMergeCandidatePaths);
    }

    [McpServerTool]
    [Description("Return evaluated self-check guardrails for configured roots, working folders, diff tool availability, and watched-source safety boundaries.")]
    public AICodingServicesSelfCheckResult GetSelfCheck()
    {
        runtimeState.Touch();
        AICodingServicesGuardrailCheck[] guardrails = BuildSelfCheckGuardrails();
        string overallStatus = guardrails.Any(check => check.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)) ? "failed"
            : guardrails.Any(check => check.Status.Equals("warning", StringComparison.OrdinalIgnoreCase)) ? "warning"
            : guardrails.Any(check => check.Status.Equals("unavailable", StringComparison.OrdinalIgnoreCase)) ? "unavailable"
            : "passed";
        return new AICodingServicesSelfCheckResult(
            settings.RepositoryRoot,
            settings.RuntimeRoot,
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            workflowPaths.WorkingRoot,
            workflowPaths.HistoryRoot,
            workflowPaths.StagedRoot,
            File.Exists(settings.WatchedSolutionPath),
            Directory.Exists(settings.WatchedProjectFolder),
            settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists),
            "agents edit monitor-owned Working candidates only; WinMerge review/save remains the watched-source mutation surface",
            overallStatus,
            guardrails);
    }

    [McpServerTool]
    [Description("Create a durable monitor session handle and declare the watched files planned for this edit session.")]
    public AICodingServicesSessionState StartMonitorSession(
        [Description("Planned watched files and their MSBuild owning projects. At least one file is required.")] IReadOnlyList<AICodingServicesSessionPlannedFileInput> filesPlanned,
        [Description("Short purpose for this monitor session.")] string purpose = "monitor workflow")
    {
        runtimeState.Touch();
        IReadOnlyList<AICodingServicesSessionPlannedFile> plannedFiles = BuildPlannedFiles(filesPlanned);
        AICodingServicesSessionState session = new(
            $"session-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..48],
            purpose,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [])
        {
            EditPlan = new AICodingServicesSessionEditPlan(DateTimeOffset.UtcNow, plannedFiles)
        };
        SaveSession(session);
        RecordMonitorSessionEvent(
            session.SessionId,
            "start-monitor-session",
            $"{plannedFiles.Count} planned file(s)",
            JsonSerializer.Serialize(session.EditPlan, JsonOptions));
        return session;
    }

    [McpServerTool]
    [Description("Replace the watched files planned for this monitor edit session after explicit operator correction.")]
    public AICodingServicesSessionState SetMonitorSessionEditPlan(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Planned watched files and their MSBuild owning projects.")] IReadOnlyList<AICodingServicesSessionPlannedFileInput> filesPlanned)
    {
        runtimeState.Touch();
        AICodingServicesSessionState session = GetMonitorSession(sessionId);
        IReadOnlyList<AICodingServicesSessionPlannedFile> plannedFiles = BuildPlannedFiles(filesPlanned);

        AICodingServicesSessionState updated = session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            EditPlan = new AICodingServicesSessionEditPlan(DateTimeOffset.UtcNow, plannedFiles)
        };
        SaveSession(updated);
        RecordMonitorSessionEvent(
            sessionId,
            "set-monitor-session-edit-plan",
            $"{plannedFiles.Count} planned file(s)",
            JsonSerializer.Serialize(updated.EditPlan, JsonOptions));
        return updated;
    }

    [McpServerTool]
    [Description("List durable monitor session handles known to this MCP server.")]
    public IReadOnlyList<AICodingServicesSessionSummary> ListMonitorSessions()
    {
        runtimeState.Touch();
        return Directory.Exists(SessionRoot)
            ? Directory.EnumerateFiles(SessionRoot, "*.json")
                .Select(LoadSession)
                .Where(session => session is not null)
                .Select(session => new AICodingServicesSessionSummary(session!.SessionId, session.Purpose, session.CreatedAtUtc, session.UpdatedAtUtc, session.Events.Count))
                .OrderByDescending(session => session.UpdatedAtUtc)
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Return a durable monitor session by explicit sessionId handle.")]
    public AICodingServicesSessionState GetMonitorSession(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        return LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
    }

    [McpServerTool]
    [Description("Append an event to a durable monitor session.")]
    public AICodingServicesSessionState RecordMonitorSessionEvent(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Short event type, such as user-message, tool-call, tool-result, final-answer, or error.")] string eventType,
        [Description("Human-readable event summary.")] string summary,
        [Description("Optional JSON payload for the event.")] string? payloadJson = null)
    {
        runtimeState.Touch();
        AICodingServicesSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        List<AICodingServicesSessionEvent> events = session.Events.ToList();
        events.Add(new AICodingServicesSessionEvent(DateTimeOffset.UtcNow, eventType, summary, payloadJson));
        AICodingServicesSessionState updated = session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Events = events
        };
        SaveSession(updated);
        return updated;
    }

    [McpServerTool]
    [Description("List staged edit records owned by a durable monitor session.")]
    public IReadOnlyList<StagedEditRecord> ListSessionStagedRecords(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        _ = GetMonitorSession(sessionId);
        return workflowService.ListStagedRecords(sessionId);
    }

}
