using System.Text;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;

namespace CodexAppServerBlazor.Services.ArchivedDiscussions;

public class ArchivedDiscussionService : IArchivedDiscussionService
{
    private readonly CodingServicesSettingsProvider settingsProvider;

    public ArchivedDiscussionService(CodingServicesSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public ArchivedDiscussionSaveResult SaveDiscussion(ArchivedDiscussionSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot))
        {
            throw new InvalidOperationException("Workspace root is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Archived discussion name is required.");
        }

        string archiveName = NormalizeArchiveName(request.Name);

        string transcript = request.TranscriptText?.Trim() ?? string.Empty;
        string draft = request.DraftText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(transcript) &&
            string.IsNullOrWhiteSpace(draft) &&
            (request.AttachmentNames?.Count ?? 0) == 0)
        {
            throw new InvalidOperationException("There is no discussion content to archive.");
        }

        CodingServicesSettings settings = settingsProvider.GetSettings(request.WorkspaceRoot);
        string archiveRoot = Path.Combine(
            SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "planning",
            "archived-discussions");
        string slug = Slugify(archiveName);
        string timestamp = request.CapturedAt.ToString("yyyyMMdd-HHmmss");
        string folderPath = Path.Combine(archiveRoot, timestamp + "-" + slug + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folderPath);

        string markdownPath = Path.Combine(folderPath, "discussion.md");
        try
        {
            File.WriteAllText(markdownPath, BuildMarkdown(request with { Name = archiveName }, transcript, draft), Encoding.UTF8);

            WorkflowTaskBoardRepository repository = CreateRepository(settings);
            ArchivedDiscussionRow row = SaveArchivedDiscussionRow(
                repository,
                archiveName,
                markdownPath,
                request.ThreadId,
                request.TurnMode,
                request.Trigger);

            return new ArchivedDiscussionSaveResult(
                row.Id,
                row.Name,
                row.MarkdownPath);
        }
        catch (Exception ex)
        {
            Exception? cleanupException = TryDeleteArchiveFolder(folderPath);
            if (cleanupException is null)
            {
                throw;
            }

            throw new InvalidOperationException(
                "Archived discussion save failed, and cleanup of the partially written archive also failed. See the inner exception chain for both errors.",
                new AggregateException(ex, cleanupException));
        }
    }

    protected virtual WorkflowTaskBoardRepository CreateRepository(CodingServicesSettings settings)
    {
        return new WorkflowTaskBoardRepository(
            SystemDataPaths.GetDefaultPlanningDatabasePath(settings),
            SystemDataPaths.GetDefaultTaskMemoryRoot(settings));
    }

    protected virtual ArchivedDiscussionRow SaveArchivedDiscussionRow(
        WorkflowTaskBoardRepository repository,
        string name,
        string markdownPath,
        string? threadId,
        string turnMode,
        string trigger)
    {
        return repository.SaveArchivedDiscussion(
            name,
            markdownPath,
            threadId,
            turnMode,
            trigger);
    }

    private static string NormalizeArchiveName(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Archived discussion name is required.");
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
            {
                throw new InvalidOperationException("Archived discussion name cannot contain control characters.");
            }
        }

        return trimmed.Length <= 200
            ? trimmed
            : trimmed[..200].TrimEnd();
    }

    private static string BuildMarkdown(
        ArchivedDiscussionSaveRequest request,
        string transcript,
        string draft)
    {
        StringBuilder builder = new();
        builder.AppendLine("# " + request.Name.Trim());
        builder.AppendLine();
        builder.AppendLine("- Captured: " + request.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
        builder.AppendLine("- Workspace: " + Path.GetFullPath(request.WorkspaceRoot));
        builder.AppendLine("- Trigger: " + request.Trigger);
        builder.AppendLine("- Turn mode: " + request.TurnMode);
        builder.AppendLine("- Thread: " + (string.IsNullOrWhiteSpace(request.ThreadId) ? "not started" : request.ThreadId));
        builder.AppendLine();

        if (request.AttachmentNames.Count > 0)
        {
            builder.AppendLine("## Pending Attachments");
            builder.AppendLine();
            foreach (string attachmentName in request.AttachmentNames)
            {
                builder.AppendLine("- " + attachmentName);
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(draft))
        {
            builder.AppendLine("## Unsent Draft");
            builder.AppendLine();
            builder.AppendLine(draft);
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(transcript))
        {
            builder.AppendLine("## Transcript");
            builder.AppendLine();
            builder.AppendLine(transcript);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string Slugify(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "discussion" : slug;
    }

    private static Exception? TryDeleteArchiveFolder(string folderPath)
    {
        try
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }

            return null;
        }
        catch (IOException ex)
        {
            return ex;
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex;
        }
    }
}
