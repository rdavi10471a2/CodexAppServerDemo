using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.ArchivedDiscussions;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class ArchivedDiscussionServiceTests
{
    [Fact]
    public void SaveDiscussion_writes_markdown_and_registers_row_in_planning_database()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettingsProvider provider = CreateProvider();
        ArchivedDiscussionService service = new(provider);

        ArchivedDiscussionSaveResult result = service.SaveDiscussion(
            new ArchivedDiscussionSaveRequest(
                repository.RootPath,
                "Review conversation about approval prompts",
                "thread-123",
                "Discuss",
                "Start New Conversation",
                "You 10:00:00" + Environment.NewLine + "We should improve approvals.",
                "unsent follow-up",
                ["spec.md"],
                DateTimeOffset.Parse("2026-07-01T10:30:00-05:00")));

        Assert.True(File.Exists(result.MarkdownPath));
        string markdown = File.ReadAllText(result.MarkdownPath);
        Assert.Contains("# Review conversation about approval prompts", markdown, StringComparison.Ordinal);
        Assert.Contains("## Transcript", markdown, StringComparison.Ordinal);
        Assert.Contains("## Unsent Draft", markdown, StringComparison.Ordinal);
        Assert.Contains("## Pending Attachments", markdown, StringComparison.Ordinal);

        CodingServicesSettings settings = provider.GetSettings(repository.RootPath);
        WorkflowTaskBoardRepository taskBoardRepository = new(
            SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
            SystemDataPaths.GetDefaultTaskMemoryRoot(settings));

        ArchivedDiscussionRow row = Assert.Single(taskBoardRepository.ListArchivedDiscussions());
        Assert.Equal(result.Id, row.Id);
        Assert.Equal(result.Name, row.Name);
        Assert.Equal(result.MarkdownPath, row.MarkdownPath);
        Assert.Equal("thread-123", row.ThreadId);
        Assert.Equal("Discuss", row.TurnMode);
        Assert.Equal("Start New Conversation", row.Trigger);

        string archiveRoot = Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "archived-discussions");
        Assert.StartsWith(archiveRoot, result.MarkdownPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("discussion.md", Path.GetFileName(result.MarkdownPath));
    }

    [Fact]
    public void SaveDiscussion_trims_and_limits_name_before_persisting()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettingsProvider provider = CreateProvider();
        ArchivedDiscussionService service = new(provider);
        string oversizedName = "  " + new string('a', 240) + "  ";

        ArchivedDiscussionSaveResult result = service.SaveDiscussion(
            new ArchivedDiscussionSaveRequest(
                repository.RootPath,
                oversizedName,
                null,
                "Discuss",
                "Manual",
                "You 10:00:00" + Environment.NewLine + "archive me",
                string.Empty,
                [],
                DateTimeOffset.Parse("2026-07-01T10:30:00-05:00")));

        Assert.Equal(200, result.Name.Length);
        Assert.False(result.Name.StartsWith(" ", StringComparison.Ordinal));
        Assert.False(result.Name.EndsWith(" ", StringComparison.Ordinal));
        Assert.Contains("# " + result.Name, File.ReadAllText(result.MarkdownPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveDiscussion_rejects_control_characters_in_name()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettingsProvider provider = CreateProvider();
        ArchivedDiscussionService service = new(provider);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.SaveDiscussion(
            new ArchivedDiscussionSaveRequest(
                repository.RootPath,
                "Bad" + '\u0007' + "Name",
                null,
                "Discuss",
                "Manual",
                "You 10:00:00" + Environment.NewLine + "archive me",
                string.Empty,
                [],
                DateTimeOffset.Parse("2026-07-01T10:30:00-05:00"))));

        Assert.Contains("control characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveDiscussion_deletes_written_archive_folder_when_persistence_fails()
    {
        using TemporaryRepository repository = TemporaryRepository.Create();
        CodingServicesSettingsProvider provider = CreateProvider();
        ThrowingArchivedDiscussionService service = new(provider);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => service.SaveDiscussion(
            new ArchivedDiscussionSaveRequest(
                repository.RootPath,
                "Fail after file write",
                "thread-123",
                "Discuss",
                "Stop Server",
                "You 10:00:00" + Environment.NewLine + "archive me",
                string.Empty,
                [],
                DateTimeOffset.Parse("2026-07-01T10:30:00-05:00"))));

        Assert.Equal("Simulated persistence failure.", ex.Message);
        string archiveRoot = Path.Combine(repository.RootPath, "planning", "archived-discussions");
        Assert.False(Directory.Exists(archiveRoot) && Directory.EnumerateFileSystemEntries(archiveRoot).Any());
    }

    private static CodingServicesSettingsProvider CreateProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodingServices:RuntimeRoot"] = "runtime",
                ["CodingServices:WatchedSolutionPath"] = "CodexAppServerWinForms_corrected.slnx"
            })
            .Build();

        return new CodingServicesSettingsProvider(configuration);
    }

    private sealed class ThrowingArchivedDiscussionService : ArchivedDiscussionService
    {
        public ThrowingArchivedDiscussionService(CodingServicesSettingsProvider settingsProvider)
            : base(settingsProvider)
        {
        }

        protected override ArchivedDiscussionRow SaveArchivedDiscussionRow(
            WorkflowTaskBoardRepository repository,
            string name,
            string markdownPath,
            string? threadId,
            string turnMode,
            string trigger)
        {
            throw new InvalidOperationException("Simulated persistence failure.");
        }
    }
}
