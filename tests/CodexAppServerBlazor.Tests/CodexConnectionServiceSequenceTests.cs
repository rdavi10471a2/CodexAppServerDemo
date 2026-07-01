using System.Reflection;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class CodexConnectionServiceSequenceTests
{
    [Fact]
    public async Task Client_exit_event_clears_running_turn_state()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodexConnectionService service = CreateService(repository.RootPath);

        try
        {
            InvokePrivate(service, "MarkTurnRunning");
            Assert.True(service.GetSnapshot().IsTurnRunning);

            InvokePrivate(service, "OnClientExited", 23);

            CodexConnectionSnapshot snapshot = service.GetSnapshot();
            Assert.False(snapshot.IsTurnRunning);
            CodexOutputEvent serverExit = Assert.Single(snapshot.StatusEvents, e => e.Type == "ServerExited");
            Assert.Equal("error", serverExit.Status);
            Assert.Contains("23", serverExit.Detail, StringComparison.Ordinal);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task Matching_command_completion_marks_approved_command_request_complete()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodexConnectionService service = CreateService(repository.RootPath);

        try
        {
            InvokePrivate(
                service,
                "OnServerRequest",
                new CodexServerRequestEvent(
                    41,
                    "item/commandExecution/requestApproval",
                    "Approval requested for command: dotnet test",
                    "{}",
                    "cmd-41",
                    null));
            PermissionRequestService permissionService = GetPermissionRequestService(service);
            Assert.NotNull(permissionService.Resolve(41, "approved"));
            InvokePrivate(
                service,
                "OnToolActivity",
                new ToolEvent(
                    "command completed",
                    "dotnet test",
                    "completed",
                    "ok",
                    "cmd-41",
                    null));

            CodexConnectionSnapshot snapshot = service.GetSnapshot();
            CodexPermissionRequest request = Assert.Single(snapshot.PermissionRequests);
            Assert.Equal("command completed", request.Status);
            Assert.Contains("command completed", snapshot.CurrentTurnNoticeText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public void Missing_correlation_skips_non_command_permission_requests()
    {
        PermissionRequestService service = new();

        service.Add(new CodexServerRequestEvent(
            51,
            "item/permissions/requestApproval",
            "Need network",
            "{}",
            "perm-51",
            null));
        service.Add(new CodexServerRequestEvent(
            52,
            "item/commandExecution/requestApproval",
            "Run command",
            "{}",
            "cmd-52",
            null));

        Assert.NotNull(service.Resolve(51, "approved"));
        Assert.NotNull(service.Resolve(52, "approved"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult(null, null, "command completed");

        Assert.NotNull(resolved);
        Assert.Equal(52, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().OrderBy(r => r.RequestId).ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }

    [Fact]
    public void Wrong_correlation_falls_back_to_latest_approved_request()
    {
        PermissionRequestService service = new();

        service.Add(new CodexServerRequestEvent(
            61,
            "item/commandExecution/requestApproval",
            "First command",
            "{}",
            "cmd-61",
            null));
        service.Add(new CodexServerRequestEvent(
            62,
            "item/commandExecution/requestApproval",
            "Second command",
            "{}",
            "cmd-62",
            null));

        Assert.NotNull(service.Resolve(61, "approved"));
        Assert.NotNull(service.Resolve(62, "approved"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult("cmd-missing", null, "command failed");

        Assert.NotNull(resolved);
        Assert.Equal(62, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().OrderBy(r => r.RequestId).ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command failed", snapshot[1].Status);
    }

    [Fact]
    public void Matching_command_completion_uses_approval_id_when_item_id_is_shared()
    {
        PermissionRequestService service = new();

        service.Add(new CodexServerRequestEvent(
            71,
            "item/commandExecution/requestApproval",
            "First subcommand callback",
            "{}",
            "cmd-shared",
            "approval-1"));
        service.Add(new CodexServerRequestEvent(
            72,
            "item/commandExecution/requestApproval",
            "Second subcommand callback",
            "{}",
            "cmd-shared",
            "approval-2"));

        Assert.NotNull(service.Resolve(71, "approved"));
        Assert.NotNull(service.Resolve(72, "approved"));

        CodexPermissionRequest? resolved = service.ResolveCommandResult("cmd-shared", "approval-2", "command completed");

        Assert.NotNull(resolved);
        Assert.Equal(72, resolved.RequestId);
        CodexPermissionRequest[] snapshot = service.GetSnapshot().OrderBy(r => r.RequestId).ToArray();
        Assert.Equal("approved", snapshot[0].Status);
        Assert.Equal("command completed", snapshot[1].Status);
    }

    [Fact]
    public async Task Matching_file_change_completion_marks_approved_request_complete()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodexConnectionService service = CreateService(repository.RootPath);

        try
        {
            InvokePrivate(
                service,
                "OnServerRequest",
                new CodexServerRequestEvent(
                    81,
                    "item/fileChange/requestApproval",
                    "Approval requested for file change",
                    "{}",
                    "patch-81",
                    null));
            PermissionRequestService permissionService = GetPermissionRequestService(service);
            Assert.NotNull(permissionService.Resolve(81, "approved"));
            InvokePrivate(
                service,
                "OnToolActivity",
                new ToolEvent(
                    "file change completed",
                    "file change",
                    "completed",
                    "[]",
                    "patch-81",
                    null));

            CodexConnectionSnapshot snapshot = service.GetSnapshot();
            CodexPermissionRequest request = Assert.Single(snapshot.PermissionRequests);
            Assert.Equal("change completed", request.Status);
            Assert.Contains("change completed", snapshot.CurrentTurnNoticeText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task Failed_command_completion_surfaces_diagnostic_notice_and_status_detail()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodexConnectionService service = CreateService(repository.RootPath);

        try
        {
            InvokePrivate(
                service,
                "OnServerRequest",
                new CodexServerRequestEvent(
                    91,
                    "item/commandExecution/requestApproval",
                    "Approval requested for command: rg test",
                    "{}",
                    "cmd-91",
                    null));
            PermissionRequestService permissionService = GetPermissionRequestService(service);
            Assert.NotNull(permissionService.Resolve(91, "approved"));
            InvokePrivate(
                service,
                "OnToolActivity",
                new ToolEvent(
                    "command completed",
                    "rg test",
                    "failed",
                    "cwd: C:\\Work | exit: 1" + Environment.NewLine + Environment.NewLine + "CreateProcessWithLogonW failed: 2",
                    "cmd-91",
                    null));

            CodexConnectionSnapshot snapshot = service.GetSnapshot();
            CodexPermissionRequest request = Assert.Single(snapshot.PermissionRequests);
            Assert.Equal("command failed", request.Status);
            Assert.Contains("Diagnostic: cwd: C:\\Work | exit: 1", snapshot.CurrentTurnNoticeText, StringComparison.Ordinal);
            CodexOutputEvent permissionResult = Assert.Single(snapshot.StatusEvents, e => e.Type == "PermissionResult");
            Assert.Contains("CreateProcessWithLogonW failed: 2", permissionResult.Detail, StringComparison.Ordinal);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    private static CodexConnectionService CreateService(string workspaceRoot)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "Sample.slnx"
            })
            .Build();

        string solutionPath = Path.Combine(workspaceRoot, "Sample.slnx");
        if (!File.Exists(solutionPath))
        {
            File.WriteAllText(solutionPath, "<Solution />");
        }

        WorkspaceState workspaceState = new();
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        SourceWorkspaceService sourceWorkspaceService = new(settingsProvider);
        WorkspaceWorkflowContextService workspaceWorkflowContextService = new(sourceWorkspaceService);
        TaskWorkflowContextService taskWorkflowContextService = new(settingsProvider);
        WorkflowTurnContextComposer workflowTurnContextComposer = new();
        return new CodexConnectionService(
            workspaceState,
            workspaceWorkflowContextService,
            taskWorkflowContextService,
            workflowTurnContextComposer);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: " + methodName);
        method.Invoke(target, arguments);
    }

    private static PermissionRequestService GetPermissionRequestService(CodexConnectionService service)
    {
        FieldInfo field = typeof(CodexConnectionService).GetField("permissionRequestService", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Field not found: permissionRequestService");
        return (PermissionRequestService)(field.GetValue(service)
            ?? throw new InvalidOperationException("permissionRequestService was null."));
    }
}
