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
    private const string RazorRelativePath = "Components/Layout/MainLayout.razor";

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
        RecordValidationFailure(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        IReadOnlyList<StagedReviewQueueItem> pending = tools.ListPendingStagedReviews().GetAwaiter().GetResult();

        StagedReviewQueueItem item = Assert.Single(pending);
        Assert.Equal(record.StagedRecordId, item.StagedRecordId);
        Assert.Equal("Example.cs", item.RelativePath);
    }

    [Fact]
    public void AcceptStagedReview_throws_when_terminal_validation_fails_before_copy()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(
            repository.RootPath,
            "Example.cs",
            "namespace Example; public sealed class Example { public int Value => 1; }");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(
            status.WorkingFilePath,
            "namespace Example; public sealed class Example { public int Value => 2; }");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            tools.AcceptStagedReview(record.StagedRecordId).GetAwaiter().GetResult());

        Assert.Contains("Terminal planned pre-merge validation failed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(
            "namespace Example; public sealed class Example { public int Value => 1; }",
            File.ReadAllText(sourcePath));
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

        StagedReviewPageActionResult result = tools.RejectStagedReview(record.StagedRecordId).GetAwaiter().GetResult();

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

        StagedReviewPageModel firstModel = tools.LoadNextSessionReview(sessionId).GetAwaiter().GetResult();
        tools.AcceptStagedReview(firstModel.StagedRecordId).GetAwaiter().GetResult();
        StagedReviewPageModel secondModel = tools.LoadNextSessionReview(sessionId).GetAwaiter().GetResult();

        Assert.Equal(firstRecord.StagedRecordId, firstModel.StagedRecordId);
        Assert.Equal(secondRecord.StagedRecordId, secondModel.StagedRecordId);
    }

    [Fact]
    public async Task StageEditSessionForReview_fails_closed_when_file_session_does_not_match_requested_session()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "Components/Layout/MainLayout.razor", "<h1>One</h1>");
        string secondPath = CreateWatchedFile(repository.RootPath, "Components/Pages/Home.razor", "<p>Two</p>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath, first.EditSessionId);
        workflowService.DeclareSessionFiles(first.EditSessionId, [firstPath, secondPath]);
        EditSessionStatus mismatchedSecond = workflowService.Refresh(secondPath, "edit-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(first.WorkingFilePath, "<h1>Changed</h1>");
        File.WriteAllText(mismatchedSecond.WorkingFilePath, "<p>Changed</p>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Accepted);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StageEditSessionForReviewAsync(elicitor, first.EditSessionId));

        Assert.Contains("is bound to edit session", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(first.EditSessionId, ex.Message, StringComparison.Ordinal);
        Assert.Contains(mismatchedSecond.EditSessionId, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageEditSessionForReview_stages_declared_files_under_one_review_session()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "Components/Layout/MainLayout.razor", "<h1>One</h1>");
        string secondPath = CreateWatchedFile(repository.RootPath, "Components/Pages/Home.razor", "<p>Two</p>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath, first.EditSessionId);
        workflowService.DeclareSessionFiles(first.EditSessionId, [firstPath, secondPath]);
        File.WriteAllText(first.WorkingFilePath, "<h1>Changed One</h1>");
        File.WriteAllText(second.WorkingFilePath, "<p>Changed Two</p>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Accepted);

        StageForReviewResult result = await service.StageEditSessionForReviewAsync(elicitor, first.EditSessionId);
        IReadOnlyList<StagedReviewQueueItem> pending = service.ListPending();

        Assert.Equal(1, elicitor.CallCount);
        Assert.Equal(2, pending.Count(item => item.SessionId.Equals(first.EditSessionId, StringComparison.Ordinal)));
        Assert.Contains($"/review/session/{first.EditSessionId}", result.ReviewUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageEditSessionForReview_fails_when_declared_file_was_not_bootstrapped_into_session()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "Components/Layout/MainLayout.razor", "<h1>One</h1>");
        string secondPath = CreateWatchedFile(repository.RootPath, "Components/Pages/Home.razor", "<p>Two</p>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        workflowService.DeclareSessionFiles(first.EditSessionId, [firstPath]);

        EditSessionPlan plan = workflowService.GetSessionPlan(first.EditSessionId)!;
        plan.DeclaredWatchedFilePaths.Add(Path.GetFullPath(secondPath));
        plan.DeclaredRelativePaths.Add("Components/Pages/Home.razor");
        WorkflowEditPaths paths = new(CodingServicesSettings.Create(
            repository.RootPath,
            projectPath,
            runtimeRoot: Path.Combine(repository.RootPath, "runtime-test")));
        string sessionPlanPath = paths.GetSessionPlanPath(first.EditSessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPlanPath)!);
        File.WriteAllText(
            sessionPlanPath,
            System.Text.Json.JsonSerializer.Serialize(
                plan,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Accepted);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StageEditSessionForReviewAsync(elicitor, first.EditSessionId));

        Assert.Contains("Run refresh_file", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Home.razor", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptStagedReview_defers_index_refresh_until_last_file_in_session()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "namespace Example; public sealed class First { public int Value => 1; }");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "namespace Example; public sealed class Second { public int Value => 2; }");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, projectPath);
        EditSessionStatus first = workflowService.Refresh(firstPath, sessionId);
        EditSessionStatus second = workflowService.Refresh(secondPath, sessionId);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value => 11; }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value => 22; }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        WorkspaceReviewMcpTools tools = CreateTools(repository.RootPath);

        StagedReviewPageActionResult firstAccept = tools.AcceptStagedReview(firstRecord.StagedRecordId).GetAwaiter().GetResult();

        Assert.Contains("deferred", firstAccept.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("namespace Example; public sealed class First { public int Value => 11; }", File.ReadAllText(firstPath));
    }

    [Fact]
    public async Task StageEditSessionForReview_raises_elicitation_with_review_context_for_queue_of_one()
    {
        // The elicitation is the block; per-file accept/reject is applied by the host review dialog while the
        // call is suspended (covered separately by the AcceptStagedReview tool tests). Here we verify the gate
        // raises the elicitation with the correct session/review context and reports session completion.
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =3</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Accepted);

        StageForReviewResult result = await service.StageEditSessionForReviewAsync(elicitor, status.EditSessionId);

        Assert.Equal(1, elicitor.CallCount);
        Assert.NotNull(elicitor.LastRequest);
        Assert.Equal(status.EditSessionId, elicitor.LastRequest!.SessionId);
        Assert.Contains("MainLayout.razor", elicitor.LastRequest.RelativePath, StringComparison.Ordinal);
        Assert.Contains($"/review/session/{status.EditSessionId}", elicitor.LastRequest.ReviewUrl, StringComparison.Ordinal);
        Assert.False(elicitor.LastRequest.ValidationIsError);
        Assert.Contains("completed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StageEditSessionForReview_declined_decision_reports_declined_for_queue_of_one()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =5</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Rejected);

        StageForReviewResult result = await service.StageEditSessionForReviewAsync(elicitor, status.EditSessionId);

        Assert.Equal(1, elicitor.CallCount);
        Assert.Contains("declined", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<h1>Schema Studio Web</h1>", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task StageEditSessionForReview_requires_explicit_session_id()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =6</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Rejected);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StageEditSessionForReviewAsync(elicitor, ""));

        Assert.Contains("requires a non-empty sessionId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StageEditSessionForReview_cancel_decision_throws_and_leaves_source_unchanged_for_queue_of_one()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =7</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        StubReviewElicitor elicitor = new(ReviewDecision.Cancelled);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.StageEditSessionForReviewAsync(elicitor, status.EditSessionId));

        Assert.Equal("<h1>Schema Studio Web</h1>", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task StageEditSessionForReview_blocks_until_elicitation_is_answered_for_queue_of_one()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =8</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        GatedReviewElicitor elicitor = new();

        Task<StageForReviewResult> stageTask = service.StageEditSessionForReviewAsync(elicitor, status.EditSessionId);
        await elicitor.Raised;
        await Task.Delay(150);
        Assert.False(stageTask.IsCompleted, "Stage must block while the elicitation is unanswered.");

        elicitor.Complete(ReviewDecision.Accepted);
        StageForReviewResult result = await stageTask;
        Assert.Contains("completed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StageEditSessionForReview_honors_cancellation_token_for_queue_of_one()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, RazorRelativePath, "<h1>Schema Studio Web</h1>");

        WorkflowEditService workflowService = CreateWorkflowService(repository.RootPath, Path.Combine(repository.RootPath, "Example.csproj"));
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        workflowService.DeclareSessionFiles(status.EditSessionId, [sourcePath]);
        File.WriteAllText(status.WorkingFilePath, "<h1>Schema Studio Web <span style=\"color: red;\">--Coding Services =9</span></h1>");

        HarnessWorkspaceReviewService service = CreateReviewService(repository.RootPath);
        GatedReviewElicitor elicitor = new();
        using CancellationTokenSource cts = new();

        Task<StageForReviewResult> stageTask = service.StageEditSessionForReviewAsync(elicitor, status.EditSessionId, cancellationToken: cts.Token);
        await elicitor.Raised;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stageTask);
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

        StagedReviewPageActionResult result = tools.AcceptStagedReview(record.StagedRecordId, forceApproveValidation: true).GetAwaiter().GetResult();

        Assert.Equal("proposed", File.ReadAllText(sourcePath));
        Assert.True(result.Model.PreMergeValidationForceApproved);
    }

    private sealed class StubReviewElicitor : IReviewElicitor
    {
        private readonly ReviewDecision decision;

        public StubReviewElicitor(ReviewDecision decision)
        {
            this.decision = decision;
        }

        public int CallCount { get; private set; }

        public ReviewElicitationRequest? LastRequest { get; private set; }

        public Task<ReviewDecision> RequestDecisionAsync(ReviewElicitationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(decision);
        }
    }

    private sealed class GatedReviewElicitor : IReviewElicitor
    {
        private readonly TaskCompletionSource<ReviewDecision> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource raisedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Raised => raisedSource.Task;

        public void Complete(ReviewDecision decision)
        {
            gate.TrySetResult(decision);
        }

        public async Task<ReviewDecision> RequestDecisionAsync(ReviewElicitationRequest request, CancellationToken cancellationToken)
        {
            raisedSource.TrySetResult();
            using (cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken)))
            {
                return await gate.Task;
            }
        }
    }

    private static HarnessWorkspaceReviewService CreateReviewService(string workspaceRoot, string? editSessionId = null)
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
        CodingServicesSettingsProvider settingsProvider = TestServiceFactory.CreateSettingsProvider(configuration, workspaceRoot);
        return new HarnessWorkspaceReviewService(workspaceState, settingsProvider);
    }

    private static WorkspaceReviewMcpTools CreateTools(string workspaceRoot)
    {
        return new WorkspaceReviewMcpTools(CreateReviewService(workspaceRoot));
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
