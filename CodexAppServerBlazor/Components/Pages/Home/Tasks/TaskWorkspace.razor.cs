using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace CodexAppServerBlazor.Components.Pages.Home.Tasks;

public partial class TaskWorkspace : ComponentBase
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    private string taskName = string.Empty;
    private string taskDescription = string.Empty;
    private string notesMarkdown = string.Empty;
    private string newComment = string.Empty;
    private string selectedPane = "Task";
    private string selectedStateCode = string.Empty;
    private string? loadedTaskStateKey;

    [Parameter]
    public TaskBoardTaskDetailViewModel? Task { get; set; }

    [Parameter]
    public IReadOnlyList<TaskBoardColumnViewModel> StateOptions { get; set; } = [];

    [Parameter]
    public bool IsNavigatorVisible { get; set; }

    [Parameter]
    public EventCallback<TaskDetailsSaveRequest> OnSaveDetails { get; set; }

    [Parameter]
    public EventCallback<string> OnSaveNotes { get; set; }

    [Parameter]
    public EventCallback<string> OnAddComment { get; set; }

    [Parameter]
    public EventCallback OnToggleNavigator { get; set; }

    [Parameter]
    public EventCallback OnRefresh { get; set; }

    protected override void OnParametersSet()
    {
        if (Task is null)
        {
            loadedTaskStateKey = null;
            taskName = string.Empty;
            taskDescription = string.Empty;
            notesMarkdown = string.Empty;
            selectedStateCode = string.Empty;
            return;
        }

        string stateKey = BuildTaskStateKey(Task);
        if (loadedTaskStateKey is not null
            && loadedTaskStateKey.Equals(stateKey, StringComparison.Ordinal))
        {
            return;
        }

        loadedTaskStateKey = stateKey;
        taskName = Task.Name;
        taskDescription = Task.Description;
        notesMarkdown = Task.NotesMarkdown;
        selectedStateCode = Task.IsArchived ? "Archived" : Task.StateCode;
        newComment = string.Empty;
    }

    private async Task SaveDetails()
    {
        if (Task is null)
        {
            return;
        }

        string stateCode = string.IsNullOrWhiteSpace(selectedStateCode)
            ? Task.StateCode
            : selectedStateCode;
        await OnSaveDetails.InvokeAsync(new TaskDetailsSaveRequest(Task.Id, taskName, taskDescription, null, stateCode, notesMarkdown));
    }

    private async Task SaveNotes()
    {
        if (Task is null)
        {
            return;
        }

        await OnSaveNotes.InvokeAsync(notesMarkdown);
    }

    private async Task AddComment()
    {
        if (string.IsNullOrWhiteSpace(newComment))
        {
            return;
        }

        await OnAddComment.InvokeAsync(newComment);
        newComment = string.Empty;
    }

    private Task ToggleNavigator()
    {
        return OnToggleNavigator.InvokeAsync();
    }

    private Task RefreshWorkspace()
    {
        return OnRefresh.InvokeAsync();
    }

    private void SelectPane(string pane)
    {
        selectedPane = pane;
    }

    private bool IsPane(string pane)
    {
        return selectedPane.Equals(pane, StringComparison.Ordinal);
    }

    private static string RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, MarkdownPipeline);
    }

    private static string BuildTaskStateKey(TaskBoardTaskDetailViewModel task)
    {
        return string.Join(
            "|",
            task.Id,
            task.Name,
            task.Description,
            task.StateCode,
            task.UpdatedLabel,
            task.NotesMarkdown,
            task.AgentNotesMarkdown);
    }
}
