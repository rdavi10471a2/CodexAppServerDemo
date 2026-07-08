using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Workflow;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class StagedReviewPageServiceTests
{
    private const string BuildableProjectText = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="**\*.cs" Exclude="bin\**\*.cs;obj\**\*.cs;runtime-test\**\*.cs" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void Accept_copies_staged_candidate_into_watched_source_and_records_decision()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(
            repository.RootPath,
            "Example.cs",
            "namespace Example; public sealed class Example { public int Value => 1; }");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "namespace Example; public sealed class Example { public int Value => 2; }");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        StagedReviewPageActionResult result = service.Accept(repository.RootPath, record.StagedRecordId);

        Assert.Equal("namespace Example; public sealed class Example { public int Value => 2; }", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("accepted (accepted)", result.Model.DecisionStatus);
        Assert.Contains("Index was rebuilt after accept.", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_leaves_watched_source_unchanged_and_records_decision()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordReviewReady(workflowService, record.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        StagedReviewPageActionResult result = service.Reject(repository.RootPath, record.StagedRecordId);

        Assert.Equal("original", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.Equal("rejected (rejected)", result.Model.DecisionStatus);
    }

    [Fact]
    public void LoadNextForSession_returns_next_pending_record_after_accept()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "namespace Example; public sealed class First { }");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "namespace Example; public sealed class Second { }");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value => 1; }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value => 2; }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        StagedReviewPageModel firstModel = service.LoadNextForSession(repository.RootPath, sessionId);
        service.Accept(repository.RootPath, firstModel.StagedRecordId);
        StagedReviewPageModel secondModel = service.LoadNextForSession(repository.RootPath, sessionId);

        Assert.Equal(firstRecord.StagedRecordId, firstModel.StagedRecordId);
        Assert.Equal(secondRecord.StagedRecordId, secondModel.StagedRecordId);
        Assert.False(secondModel.IsSessionComplete);
        Assert.Contains("public int Value => 2;", secondModel.ProposedText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadNextForSession_returns_session_complete_after_last_accept()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "namespace Example; public sealed class Example { }");
        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        string sessionId = status.EditSessionId;
        File.WriteAllText(status.WorkingFilePath, "namespace Example; public sealed class Example { public int Value => 2; }");
        StagedEditRecord record = workflowService.Stage(sourcePath, sessionId: sessionId);
        RecordReviewReady(workflowService, record.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        service.Accept(repository.RootPath, record.StagedRecordId);
        StagedReviewPageModel completedModel = service.LoadNextForSession(repository.RootPath, sessionId);

        Assert.True(completedModel.IsSessionComplete);
        Assert.Equal("Session complete", completedModel.DecisionStatus);
    }

    [Fact]
    public void AbandonPendingSessionArtifacts_retires_staged_records_and_removes_manifest_and_working_file()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");
        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        string sessionId = status.EditSessionId;
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath, sessionId: sessionId);
        RecordReviewReady(workflowService, record.StagedRecordId);

        int retired = workflowService.AbandonPendingSessionArtifacts(sessionId, "test cleanup");
        EditSessionStatus postStatus = workflowService.GetStatus(sourcePath);
        StagedEditRecord retiredRecord = workflowService.GetStagedRecord(record.StagedRecordId);

        Assert.Equal(1, retired);
        Assert.False(File.Exists(status.WorkingFilePath));
        Assert.False(postStatus.HasSession);
        Assert.Equal("superseded", retiredRecord.Classification);
        Assert.StartsWith("abandoned-", retiredRecord.SupersededByStagedRecordId, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_defers_index_refresh_until_terminal_session_record()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "namespace Example; public sealed class First { }");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "namespace Example; public sealed class Second { }");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "namespace Example; public sealed class First { public int Value => 1; }");
        File.WriteAllText(second.WorkingFilePath, "namespace Example; public sealed class Second { public int Value => 2; }");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        StagedReviewPageActionResult result = service.Accept(repository.RootPath, firstRecord.StagedRecordId);

        Assert.Contains("Index refresh is deferred until all declared session edit files are decided.", result.Message, StringComparison.Ordinal);
        Assert.Equal("accepted (accepted)", result.Model.DecisionStatus);
    }

    [Fact]
    public void Accept_does_not_copy_terminal_session_candidate_when_validation_fails()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "first original");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "second original");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "first proposed");
        File.WriteAllText(second.WorkingFilePath, "second proposed");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        service.Accept(repository.RootPath, firstRecord.StagedRecordId);
        File.Delete(projectPath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.Accept(repository.RootPath, secondRecord.StagedRecordId));

        Assert.Contains("Terminal planned pre-merge validation failed", ex.Message, StringComparison.Ordinal);
        Assert.Equal("second original", File.ReadAllText(secondPath));
        StagedEditRecord secondAfterFailure = workflowService.GetStagedRecord(secondRecord.StagedRecordId);
        Assert.True(string.IsNullOrWhiteSpace(secondAfterFailure.Decision));
    }

    [Fact]
    public void Accept_with_force_override_allows_failed_single_file_validation()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string sourcePath = CreateWatchedFile(repository.RootPath, "Example.cs", "original");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus status = workflowService.Refresh(sourcePath);
        File.WriteAllText(status.WorkingFilePath, "proposed");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        RecordValidationFailure(workflowService, record.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        StagedReviewPageActionResult result = service.Accept(repository.RootPath, record.StagedRecordId, forceApproveValidation: true);

        Assert.Equal("proposed", File.ReadAllText(sourcePath));
        Assert.True(result.Model.IsDecided);
        Assert.True(result.Model.PreMergeValidationForceApproved);
        Assert.Contains("explicit pre-merge validation override", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_with_force_override_allows_terminal_session_record_when_validation_fails()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "first original");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "second original");
        string sessionId = "session-" + Guid.NewGuid().ToString("N");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus first = workflowService.Refresh(firstPath);
        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(first.WorkingFilePath, "first proposed");
        File.WriteAllText(second.WorkingFilePath, "second proposed");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath, sessionId: sessionId);
        StagedEditRecord secondRecord = workflowService.Stage(secondPath, sessionId: sessionId);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        service.Accept(repository.RootPath, firstRecord.StagedRecordId);
        File.Delete(projectPath);
        RecordValidationFailure(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageActionResult result = service.Accept(repository.RootPath, secondRecord.StagedRecordId, forceApproveValidation: true);

        Assert.Equal("second proposed", File.ReadAllText(secondPath));
        Assert.True(result.Model.IsDecided);
        Assert.True(result.Model.PreMergeValidationForceApproved);
    }

    [Fact]
    public void ListPending_returns_only_undecided_unsuperseded_records()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        string projectPath = CreateProject(repository.RootPath);
        string firstPath = CreateWatchedFile(repository.RootPath, "First.cs", "first original");
        string secondPath = CreateWatchedFile(repository.RootPath, "Second.cs", "second original");

        CodingServicesSettings settings = CreateSettings(repository.RootPath, projectPath);
        WorkflowEditService workflowService = new(settings);

        EditSessionStatus first = workflowService.Refresh(firstPath);
        File.WriteAllText(first.WorkingFilePath, "first proposed");
        StagedEditRecord firstRecord = workflowService.Stage(firstPath);
        RecordReviewReady(workflowService, firstRecord.StagedRecordId);

        EditSessionStatus second = workflowService.Refresh(secondPath);
        File.WriteAllText(second.WorkingFilePath, "second proposed");
        StagedEditRecord secondRecord = workflowService.Stage(secondPath);
        RecordReviewReady(workflowService, secondRecord.StagedRecordId);

        StagedReviewPageService service = CreateService(repository.RootPath, projectPath);
        service.Reject(repository.RootPath, secondRecord.StagedRecordId);

        EditSessionStatus supersededStart = workflowService.Refresh(firstPath);
        File.WriteAllText(supersededStart.WorkingFilePath, "first proposed again");
        StagedEditRecord supersedingRecord = workflowService.Stage(firstPath);
        RecordReviewReady(workflowService, supersedingRecord.StagedRecordId);

        IReadOnlyList<StagedReviewQueueItem> pending = service.ListPending(repository.RootPath);

        StagedReviewQueueItem item = Assert.Single(pending);
        Assert.Equal(supersedingRecord.StagedRecordId, item.StagedRecordId);
        Assert.Equal("First.cs", item.RelativePath);
    }

    private static CodingServicesSettings CreateSettings(string repositoryRoot, string projectPath)
    {
        return CodingServicesSettings.Create(
            repositoryRoot,
            projectPath,
            runtimeRoot: Path.Combine(repositoryRoot, "runtime-test"));
    }

    private static StagedReviewPageService CreateService(string repositoryRoot, string projectPath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime-test",
                ["CodingServices:WatchedSolutionPath"] = projectPath
            })
            .Build();
        CodingServicesSettingsProvider provider = new(configuration);
        return new StagedReviewPageService(provider);
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
