using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;

namespace CodexAppServerBlazor.Components.Pages.Home.Tasks;

public partial class TasksTab : ComponentBase, IAsyncDisposable
{
    private TaskBoardViewModel model = TaskBoardViewModel.Empty;
    private string? selectedTaskId;
    private string? errorMessage;
    private bool isNavigatorVisible = true;
    private ElementReference taskLayout;
    private ElementReference taskNavigatorPane;
    private ElementReference taskWorkspacePane;
    private ElementReference taskSplitter;
    private IJSObjectReference? taskResizeModule;

    [Inject]
    public DialogService DialogService { get; set; } = default!;

    private IReadOnlyList<TaskBoardColumnViewModel> BoardStateOptions =>
        model.Columns.ToArray();

    [Parameter]
    public string WorkspaceRoot { get; set; } = string.Empty;

    protected override void OnParametersSet()
    {
        Refresh();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        taskResizeModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/sourceResize.js");
        if (!isNavigatorVisible)
        {
            return;
        }

        await taskResizeModule.InvokeVoidAsync(
            "attachTaskSplitter",
            taskLayout,
            taskNavigatorPane,
            taskWorkspacePane,
            taskSplitter);
    }

    private void Refresh()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            model = TaskBoardViewModel.Empty;
            errorMessage = "Workspace root is required.";
            return;
        }

        Load(selectedTaskId);
    }

    private void ToggleNavigator()
    {
        isNavigatorVisible = !isNavigatorVisible;
    }

    private async Task SelectTask(string taskId)
    {
        Load(taskId);
        await Task.CompletedTask;
    }

    private async Task CreateTask(TaskCreateRequest request)
    {
        Execute(() =>
        {
            TaskBoardTaskViewModel created = TaskBoardViewService.CreateTask(WorkspaceRoot, request.Name, request.ShortName, string.Empty);
            Load(created.Id);
        });
        await Task.CompletedTask;
    }

    private async Task MoveTask(TaskMoveRequest request)
    {
        Execute(() =>
        {
            TaskBoardViewService.MoveTask(WorkspaceRoot, request.TaskId, request.StateCode);
            Load(request.TaskId);
        });
        await Task.CompletedTask;
    }

    private async Task ArchiveTask(string taskId)
    {
        Execute(() =>
        {
            TaskBoardViewService.ArchiveTask(WorkspaceRoot, taskId);
            Load(null);
        });
        await Task.CompletedTask;
    }

    private async Task RestoreTask(string taskId)
    {
        Execute(() =>
        {
            TaskBoardViewService.RestoreTask(WorkspaceRoot, taskId);
            Load(taskId);
        });
        await Task.CompletedTask;
    }

    private async Task SaveTaskDetails(TaskDetailsSaveRequest request)
    {
        Execute(() =>
        {
            if (model.SelectedTask is not null)
            {
                if (request.StateCode.Equals("Archived", StringComparison.Ordinal))
                {
                    if (!model.SelectedTask.IsArchived)
                    {
                        TaskBoardViewService.ArchiveTask(WorkspaceRoot, request.TaskId);
                    }
                }
                else
                {
                    if (model.SelectedTask.IsArchived)
                    {
                        TaskBoardViewService.RestoreTask(WorkspaceRoot, request.TaskId);
                    }

                    if (!model.SelectedTask.StateCode.Equals(request.StateCode, StringComparison.Ordinal)
                        || model.SelectedTask.IsArchived)
                    {
                        TaskBoardViewService.MoveTask(WorkspaceRoot, request.TaskId, request.StateCode);
                    }
                }
            }

            TaskBoardViewService.UpdateTaskDetails(WorkspaceRoot, request.TaskId, request.Name, request.ShortName);
            TaskBoardViewService.UpdateNotes(WorkspaceRoot, request.TaskId, request.NotesMarkdown);
            Load(request.TaskId);
        });
        await Task.CompletedTask;
    }

    private async Task SaveNotes(string notesMarkdown)
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        string taskId = model.SelectedTask.Id;
        Execute(() =>
        {
            TaskBoardViewService.UpdateNotes(WorkspaceRoot, taskId, notesMarkdown);
            Load(taskId);
        });
        await Task.CompletedTask;
    }

    private async Task AddFile(TaskFileAddRequest request)
    {
        Execute(() =>
        {
            TaskBoardViewService.AddFile(WorkspaceRoot, request.TaskId, request.RelativePath, request.Intent, request.FileRole);
            Load(request.TaskId);
        });
        await Task.CompletedTask;
    }

    private async Task AddComment(string message)
    {
        if (model.SelectedTask is null)
        {
            return;
        }

        string taskId = model.SelectedTask.Id;
        Execute(() =>
        {
            TaskBoardViewService.AddComment(WorkspaceRoot, taskId, message);
            Load(taskId);
        });
        await Task.CompletedTask;
    }

    private async Task CopyArchivedDiscussion(string archivedDiscussionId)
    {
        if (taskResizeModule is null)
        {
            return;
        }

        string content;
        try
        {
            content = TaskBoardViewService.ReadArchivedDiscussionContent(WorkspaceRoot, archivedDiscussionId);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return;
        }

        await taskResizeModule.InvokeVoidAsync("copyTextToClipboard", content);
    }

    private async Task OpenArchivedDiscussion(TaskBoardArchivedDiscussionViewModel discussion)
    {
        string content = string.Empty;
        Execute(() =>
        {
            content = TaskBoardViewService.ReadArchivedDiscussionContent(WorkspaceRoot, discussion.Id);
        });
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return;
        }

        await DialogService.OpenAsync<ArchivedDiscussionViewerDialog>(
            "Archived Discussion",
            new Dictionary<string, object?>
            {
                [nameof(ArchivedDiscussionViewerDialog.Title)] = discussion.Name,
                [nameof(ArchivedDiscussionViewerDialog.CreatedLabel)] = discussion.CreatedLabel,
                [nameof(ArchivedDiscussionViewerDialog.ThreadId)] = discussion.ThreadId,
                [nameof(ArchivedDiscussionViewerDialog.Content)] = content
            },
            new DialogOptions
            {
                Width = "760px",
                Height = "70vh",
                CloseDialogOnEsc = true,
                CloseDialogOnOverlayClick = true,
                Resizable = true,
                Draggable = true
            });
    }

    private void Load(string? taskId)
    {
        model = TaskBoardViewService.GetBoard(WorkspaceRoot, taskId);
        selectedTaskId = model.SelectedTask?.Id;
        errorMessage = null;
    }

    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (taskResizeModule is not null)
        {
            try
            {
                await taskResizeModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}
