using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkflowEditServiceTests
{
    [Fact]
    public void Refresh_creates_working_session_for_existing_file()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = CreateWatchedFile(repository.RootPath, "Features/Sample.cs", "class Sample {}\r\n");

        EditSessionStatus status = service.Refresh(watchedFilePath);

        Assert.True(status.HasSession);
        Assert.True(status.WatchedFileExists);
        Assert.True(status.WorkingFileExists);
        Assert.False(status.IsNewFile);
        Assert.False(status.RequiresRefresh);
        Assert.Equal("unchanged", status.Classification);
        Assert.True(File.Exists(status.WorkingFilePath));
        Assert.Equal(File.ReadAllText(watchedFilePath), File.ReadAllText(status.WorkingFilePath));
    }

    [Fact]
    public void NewFile_creates_empty_working_candidate_for_missing_file()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = Path.Combine(repository.RootPath, "Features", "NewFile.cs");

        EditSessionStatus status = service.NewFile(watchedFilePath);

        Assert.True(status.HasSession);
        Assert.False(status.WatchedFileExists);
        Assert.True(status.WorkingFileExists);
        Assert.True(status.IsNewFile);
        Assert.Equal("new-file-pending", status.Classification);
        Assert.Equal(string.Empty, File.ReadAllText(status.WorkingFilePath));
    }

    [Fact]
    public void EnsureEditableSession_refreshes_existing_file_when_session_missing()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = CreateWatchedFile(repository.RootPath, "Models/Widget.cs", "class Widget {}\n");

        EditSessionStatus status = service.EnsureEditableSession(watchedFilePath);

        Assert.True(status.HasSession);
        Assert.True(status.WorkingFileExists);
        Assert.False(status.IsNewFile);
        Assert.Equal("unchanged", status.Classification);
    }

    [Fact]
    public void ReplaceText_updates_working_candidate_and_reports_match_count()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Services/Greeter.cs",
            "public class Greeter\r\n{\r\n    public string Message => \"Hello\";\r\n}\r\n");
        EditSessionStatus status = service.Refresh(watchedFilePath);

        ReplaceTextResult result = service.ReplaceText(
            watchedFilePath,
            "\"Hello\"",
            "\"Hi\"",
            expectedMatches: 1,
            expectedWorkingHash: status.StagedHash);

        Assert.True(result.Changed);
        Assert.Equal(1, result.ActualMatches);
        Assert.Equal(1, result.ReplacementCount);
        Assert.Equal("CRLF", result.LineEnding);
        Assert.Contains("\"Hi\"", File.ReadAllText(result.WorkingFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("\"Hello\"", File.ReadAllText(result.WorkingFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceText_throws_when_expected_match_count_is_wrong()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = CreateWatchedFile(repository.RootPath, "Services/Greeter.cs", "class Greeter { string Value = \"Hello\"; }\n");
        EditSessionStatus status = service.Refresh(watchedFilePath);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.ReplaceText(
            watchedFilePath,
            "\"Hello\"",
            "\"Hi\"",
            expectedMatches: 2,
            expectedWorkingHash: status.StagedHash));

        Assert.Contains("expected 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceSpan_replaces_text_using_line_and_column_coordinates()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettings settings = CreateSettings(repository.RootPath);
        WorkflowEditService service = new(settings);
        string watchedFilePath = CreateWatchedFile(
            repository.RootPath,
            "Features/SpanSample.cs",
            "public class SpanSample\r\n{\r\n    public string Name = \"beta\";\r\n}\r\n");
        EditSessionStatus status = service.Refresh(watchedFilePath);

        EditSessionStatus updated = service.ReplaceSpan(
            watchedFilePath,
            startLine: 3,
            startColumn: 27,
            endLine: 3,
            endColumn: 31,
            newText: "delta",
            expectedWorkingHash: status.StagedHash,
            expectedOldText: "beta",
            validateOverlay: false);

        string workingText = File.ReadAllText(updated.WorkingFilePath);
        Assert.Contains("delta", workingText, StringComparison.Ordinal);
        Assert.DoesNotContain("beta", workingText, StringComparison.Ordinal);
    }

    private static CodingServicesSettings CreateSettings(string repositoryRoot)
    {
        string solutionPath = Path.Combine(repositoryRoot, "CodexAppServerWinForms_corrected.slnx");
        return CodingServicesSettings.Create(
            repositoryRoot,
            solutionPath,
            runtimeRoot: Path.Combine(repositoryRoot, "runtime-test"));
    }

    private static string CreateWatchedFile(string repositoryRoot, string relativePath, string content)
    {
        string filePath = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }
}
