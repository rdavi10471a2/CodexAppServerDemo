namespace CodexAppServerBlazor.Services.ArchivedDiscussions;

public interface IArchivedDiscussionService
{
    ArchivedDiscussionSaveResult SaveDiscussion(
        ArchivedDiscussionSaveRequest request);
}
