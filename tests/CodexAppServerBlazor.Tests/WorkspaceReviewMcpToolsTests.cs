using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkspaceReviewMcpToolsTests
{
    private const string BuildableProjectText = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    [Fact]
    public void ListPendingStagedReviews_returns_workspace_pending_queue()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        IReadOnlyList<StagedReviewQueueItem> pending = tools.ListPendingStagedReviews();

        StagedReviewQueueItem item = Assert.Single(pending);
        Assert.Equal(record.StagedRecordId, item.StagedRecordId);
        Assert.Equal("Example.cs", item.RelativePath);
    }

    [Fact]
    public void AcceptStagedReview_copies_candidate_into_source_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        StagedReviewPageActionResult result = tools.AcceptStagedReview(record.StagedRecordId);

        Assert.Equal("proposed", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("accepted (accepted)", result.Model.DecisionStatus);
    }

    [Fact]
    public void RejectStagedReview_records_decision_without_changing_source_through_tool_surface()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        StagedReviewPageActionResult result = tools.RejectStagedReview(record.StagedRecordId);

        Assert.Equal("original", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("rejected (rejected)", result.Model.DecisionStatus);
    }

    [Fact]
    public void LoadNextSessionReview_returns_next_pending_record()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "namespace Example; public sealed class First { }");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "namespace Example; public sealed class Second { }");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value => 1; }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value => 2; }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        StagedReviewPageModel firstModel = tools.LoadNextSessionReview(sessionId);
        tools.AcceptStagedReview(firstModel.StagedRecordId);
        StagedReviewPageModel secondModel = tools.LoadNextSessionReview(sessionId);

        Assert.Equal(firstRecord.StagedRecordId, firstModel.StagedRecordId);
        Assert.Equal(secondRecord.StagedRecordId, secondModel.StagedRecordId);
    }

    [Fact]
    public void StageCurrentCandidateForReview_returns_review_url_and_pending_record_for_non_csharp_files()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(
            repository.RootPath,
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =3</span></h1>");

        GovernedReviewCoordinatorService coordinator = new();
        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath, coordinator);
        Task<StageForReviewResult> stageTask = Task.Run(() => tools.StageCurrentCandidateForReview(sourcePath));
        GovernedReviewPendingRequest pendingRequest = WaitForPendingRequest(coordinator);
        coordinator.Complete(
            pendingRequest.Request.SessionId,
            new GovernedReviewResolution(
                pendingRequest.Request.SessionId,
                Completed: true,
                AcceptedWithOverride: false,
                RemainingPendingCount: 0,
                Message: "Governed review completed in host UI."));

        StageForReviewResult result = stageTask.GetAwaiter().GetResult();

        Assert.False(string.IsNullOrWhiteSpace(result.StagedRecord.StagedRecordId));
        Assert.Equal(status.EditSessionId, result.StagedRecord.SessionId);
        Assert.Contains($"/review/session/{status.EditSessionId}", result.ReviewUrl, StringComparison.Ordinal);
        Assert.Contains("workspace=", result.ReviewUrl, StringComparison.Ordinal);
        Assert.Contains("Governed review completed in host UI", result.Message, StringComparison.Ordinal);
        Assert.Equal("launched", result.StagedRecord.LaunchStatus);
        Assert.False(result.Validation.IsError);

        IReadOnlyList<StagedReviewQueueItem> pending = tools.ListPendingStagedReviews();
        StagedReviewQueueItem item = Assert.Single(pending);
        Assert.Equal(result.StagedRecord.StagedRecordId, item.StagedRecordId);
        Assert.Equal(Path.Combine("Components", "Layout", "MainLayout.razor"), item.RelativePath);
    }

    [Fact]
    public void StageCurrentCandidateForReview_uses_current_edit_session_when_tool_call_omits_session_id()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(
            repository.RootPath,
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =5</span></h1>");

        GovernedReviewCoordinatorService coordinator = new();
        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath, coordinator);
        Task<StageForReviewResult> stageTask = Task.Run(() => tools.StageCurrentCandidateForReview(sourcePath));
        GovernedReviewPendingRequest pendingRequest = WaitForPendingRequest(coordinator);
        coordinator.Complete(
            pendingRequest.Request.SessionId,
            new GovernedReviewResolution(
                pendingRequest.Request.SessionId,
                Completed: true,
                AcceptedWithOverride: false,
                RemainingPendingCount: 0,
                Message: "Governed review completed in host UI."));

        StageForReviewResult result = stageTask.GetAwaiter().GetResult();

        Assert.Equal(status.EditSessionId, result.StagedRecord.SessionId);
        Assert.Contains($"/review/session/{status.EditSessionId}", result.ReviewUrl, StringComparison.Ordinal);
        Assert.Contains("Governed review completed in host UI", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptStagedReview_with_force_override_accepts_failed_validation_record()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordValidationFailure(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        StagedReviewPageActionResult result = tools.AcceptStagedReview(record.StagedRecordId, forceApproveValidation: true);

        Assert.Equal("proposed", File.ReadAllText(sourcePath));
        Assert.True(result.Model.PreMergeValidationForceApproved);
    }

    [Fact]
    public void AcceptStagedReview_throws_when_host_owned_review_session_is_pending()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(
            repository.RootPath,
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =35</span></h1>");

        GovernedReviewCoordinatorService coordinator = new();
        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath, coordinator);
        Task<StageForReviewResult> stageTask = Task.Run(() => tools.StageCurrentCandidateForReview(sourcePath));
        GovernedReviewPendingRequest pendingRequest = WaitForPendingRequest(coordinator);
        Assert.False(stageTask.Wait(TimeSpan.FromMilliseconds(200)), "StageCurrentCandidateForReview should block until the governed review resolves.");
        StagedReviewQueueItem pendingItem = Assert.Single(tools.ListPendingStagedReviews());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            tools.AcceptStagedReview(pendingItem.StagedRecordId));

        Assert.Contains("host review dialog buttons", ex.Message, StringComparison.Ordinal);
        Assert.Equal("<h1>Schema Studio Web</h1>", File.ReadAllText(sourcePath));

        coordinator.Complete(
            pendingRequest.Request.SessionId,
            new GovernedReviewResolution(
                pendingRequest.Request.SessionId,
                Completed: false,
                AcceptedWithOverride: false,
                RemainingPendingCount: 1,
                Message: "Governed review remained pending."));

        stageTask.GetAwaiter().GetResult();
    }

    private static WorkspaceReviewMcpTools CreateTools(
        string workspaceRoot,
        GovernedReviewCoordinatorService? coordinator = null,
        string? editSessionId = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime-test",
                ["CodingServices:WatchedSolutionPath"] = "Example.csproj"
            })
            .Build();

        WorkspaceState workspaceState = new();
        workspaceState.SetRepoRoot(workspaceRoot);
        workspaceState.SetCurrentEditSessionId(editSessionId);
        CodingServicesSettingsProvider settingsProvider = new(configuration);
        HarnessWorkspaceReviewService reviewService = new(workspaceState, settingsProvider, coordinator ?? new GovernedReviewCoordinatorService());
        return new WorkspaceReviewMcpTools(reviewService);
    }

    private static GovernedReviewPendingRequest WaitForPendingRequest(GovernedReviewCoordinatorService coordinator)
    {
        bool ready = SpinWait.SpinUntil(() => coordinator.GetPendingRequest() is not null, TimeSpan.FromSeconds(5));
        Assert.True(ready, "Timed out waiting for governed review request.");
        return coordinator.GetPendingRequest()!;
    }

    private static WorkflowEditService CreateWorkflowService(string repositoryRoot, string projectPath)
    {
        CodingServicesSettings settings = CodingServicesSettings.Create(
            repositoryRoot,
            projectPath,
            runtimeRoot: Path.Combine(repositoryRoot, "runtime-test"));
        return new WorkflowEditService(settings);
    }

    private static string CreateProject(string repositoryRoot)
    {
        string projectPath = Path.Combine(repositoryRoot, "Example.csproj");
        File.WriteAllText(projectPath, BuildableProjectText);
        return projectPath;
    }

    private static string CreateWatchedFile(string repositoryRoot, string relativePath, string content)
    {
        string filePath = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static void RecordReviewReady(WorkflowEditService workflowService, string stagedRecordId)
    {
        PreMergeValidationResult validation = new()
        {
            Status = "passed",
            IsError = false,
            DiagnosticCount = 0,
            Message = "test validation passed"
        };

        workflowService.RecordPreMergeValidation(stagedRecordId, validation, forceApproved: false);
        workflowService.RecordDiffLaunch(stagedRecordId, launched: true, "test browser launch");
    }

    private static void RecordValidationFailure(WorkflowEditService workflowService, string stagedRecordId)
    {
        PreMergeValidationResult validation = new()
        {
            Status = "failed",
            IsError = true,
            DiagnosticCount = 1,
            Message = "known overlay failure"
        };

        workflowService.RecordPreMergeValidation(stagedRecordId, validation, forceApproved: false);
        workflowService.RecordDiffLaunch(stagedRecordId, launched: true, "test browser launch");
    }
}
