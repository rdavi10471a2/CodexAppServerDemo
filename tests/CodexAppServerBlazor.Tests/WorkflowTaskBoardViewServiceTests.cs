using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Data;
using CodexAppServerBlazor.AICodingServices.Workflow.Tasks;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Services.Tasks;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkflowTaskBoardViewServiceTests
{
    [Fact]
    public void CreateTask_uses_selected_workspace_runtime_paths()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            CodingServicesSettingsProvider provider = CreateProvider();
            WorkflowTaskBoardViewService service = new(provider);

            TaskBoardViewModel initialBoard = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initialBoard.SelectedTask);
            Assert.Equal("Initialize Workflow From Task Model", initialBoard.SelectedTask.Name);
            Assert.Equal("Active", initialBoard.SelectedTask.StateCode);

            TaskBoardTaskViewModel created = service.CreateTask(
                repository.RootPath,
                "Port task board",
                null,
                "Initial task notes.");
            TaskBoardViewModel board = service.GetBoard(repository.RootPath, created.Id);
            CodingServicesSettings settings = provider.GetSettings(repository.RootPath);
            string workspaceDataRoot = SystemWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);

            Assert.Equal(settings.WatchedSolutionPath, board.WatchedSolutionPath);
            Assert.Equal(SystemDataPaths.GetDefaultPlanningDatabasePath(settings), board.DatabasePath);
            Assert.Equal(SystemDataPaths.GetDefaultTaskMemoryRoot(settings), board.TaskMemoryRoot);
            Assert.StartsWith(workspaceDataRoot, board.DatabasePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(workspaceDataRoot, board.TaskMemoryRoot, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(board.DatabasePath));
            Assert.True(Directory.Exists(board.TaskMemoryRoot));
            Assert.NotNull(board.SelectedTask);
            Assert.Equal("Port task board", board.SelectedTask.Name);
            Assert.Equal(2, board.SelectedTask.TaskNumber);
            Assert.Equal("TASK-0002", board.SelectedTask.TaskLabel);
            Assert.Equal("PortTaskBoard", board.SelectedTask.ShortName);
            Assert.Equal("Initial task notes.", board.SelectedTask.NotesMarkdown);
            Assert.NotNull(board.SelectedTask.NotesMarkdownPath);
            string notesPath = board.SelectedTask.NotesMarkdownPath;
            string? notesDirectory = Path.GetDirectoryName(notesPath);
            Assert.NotNull(notesDirectory);
            Assert.StartsWith("task-0002-", Path.GetFileName(notesDirectory), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(board.SelectedTask.Id + "-user.md", Path.GetFileName(notesPath));
            Assert.True(Directory.Exists(notesDirectory));
        }
    }

    [Fact]
    public void Empty_workspace_initializes_active_task_and_later_tasks_are_new()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());

            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            TaskBoardTaskViewModel first = service.CreateTask(repository.RootPath, "First task", null, string.Empty);
            TaskBoardTaskViewModel second = service.CreateTask(repository.RootPath, "Second task", null, string.Empty);
            TaskBoardViewModel board = service.GetBoard(repository.RootPath, second.Id);

            Assert.NotNull(initial.SelectedTask);
            Assert.Equal(1, initial.SelectedTask.TaskNumber);
            Assert.Equal("TASK-0001", initial.SelectedTask.TaskLabel);
            Assert.Equal("Active", initial.SelectedTask.StateCode);
            Assert.Equal("Initialize Workflow From Task Model", initial.SelectedTask.Name);
            Assert.Equal(2, first.TaskNumber);
            Assert.Equal("TASK-0002", first.TaskLabel);
            Assert.Equal("Proposed", first.StateCode);
            Assert.Equal(3, second.TaskNumber);
            Assert.Equal("TASK-0003", second.TaskLabel);
            Assert.Equal("Proposed", second.StateCode);
            TaskBoardColumnViewModel newTaskColumn = Assert.Single(board.Columns, column => column.StateCode.Equals("Proposed", StringComparison.Ordinal));
            Assert.Equal("New Task", newTaskColumn.StateName);
            Assert.Equal(2, newTaskColumn.Tasks.Count);
        }
    }

    [Fact]
    public void Board_actions_update_selected_task_detail()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);
            service.MoveTask(repository.RootPath, initial.SelectedTask.Id, "Done");

            TaskBoardTaskViewModel created = service.CreateTask(repository.RootPath, "Add task UI", null, string.Empty);

            service.MoveTask(repository.RootPath, created.Id, "Active");
            service.UpdateTaskDetails(repository.RootPath, created.Id, "Add richer task UI", "TaskUi");
            service.UpdateNotes(repository.RootPath, created.Id, "Updated notes.");
            service.AddFile(repository.RootPath, created.Id, "CodexAppServerBlazor/Components/Pages/Home/Home.razor", "wire tab", "ui");
            service.AddComment(repository.RootPath, created.Id, "Ready for review.");

            TaskBoardViewModel board = service.GetBoard(repository.RootPath, created.Id);

            Assert.NotNull(board.SelectedTask);
            Assert.Equal("Add richer task UI", board.SelectedTask.Name);
            Assert.Equal("TaskUi", board.SelectedTask.ShortName);
            Assert.Equal("Active", board.SelectedTask.StateCode);
            Assert.Equal("Updated notes.", board.SelectedTask.NotesMarkdown);
            TaskBoardFileViewModel file = Assert.Single(board.SelectedTask.Files);
            Assert.Equal("CodexAppServerBlazor/Components/Pages/Home/Home.razor", file.RelativePath);
            Assert.Contains(board.SelectedTask.Events, taskEvent =>
                taskEvent.EventTypeCode.Equals("Comment", StringComparison.Ordinal)
                && taskEvent.Message.Equals("Ready for review.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void GetBoard_loads_agent_notes_written_directly_to_task_memory()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardTaskViewModel created = service.CreateTask(
                repository.RootPath,
                "Summarize task memory",
                null,
                "User-authored intent.");
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, created.Id);

            Assert.NotNull(initial.SelectedTask);
            Assert.NotNull(initial.SelectedTask.NotesMarkdownPath);
            string? taskDirectory = Path.GetDirectoryName(initial.SelectedTask.NotesMarkdownPath);
            Assert.NotNull(taskDirectory);
            string agentNotesPath = Path.Combine(taskDirectory, initial.SelectedTask.Id + "-agent.md");
            File.WriteAllText(agentNotesPath, "# Agent Notes" + Environment.NewLine + Environment.NewLine + "Loaded from disk.");

            TaskBoardViewModel refreshed = service.GetBoard(repository.RootPath, created.Id);

            Assert.NotNull(refreshed.SelectedTask);
            Assert.Equal(agentNotesPath, refreshed.SelectedTask.AgentNotesMarkdownPath);
            Assert.Contains("Loaded from disk.", refreshed.SelectedTask.AgentNotesMarkdown, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MoveTask_allows_only_one_active_task()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);
            TaskBoardTaskViewModel first = service.CreateTask(repository.RootPath, "First task", null, string.Empty);
            TaskBoardTaskViewModel second = service.CreateTask(repository.RootPath, "Second task", null, string.Empty);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                service.MoveTask(repository.RootPath, first.Id, "Active"));
            Assert.Contains("Only one task can be Active", ex.Message, StringComparison.Ordinal);
            Assert.Equal("Active", initial.SelectedTask.StateCode);
            Assert.Equal("Proposed", second.StateCode);
        }
    }

    [Fact]
    public void MoveTask_allows_non_active_moves_while_active_task_exists()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);
            Assert.Equal("Active", initial.SelectedTask.StateCode);
            TaskBoardTaskViewModel created = service.CreateTask(repository.RootPath, "Ready task", null, string.Empty);

            TaskBoardTaskViewModel moved = service.MoveTask(repository.RootPath, created.Id, "Ready");
            TaskBoardViewModel board = service.GetBoard(repository.RootPath, moved.Id);

            Assert.Equal("Ready", moved.StateCode);
            Assert.NotNull(board.SelectedTask);
            Assert.Equal("Ready", board.SelectedTask.StateCode);
            TaskBoardColumnViewModel activeColumn = Assert.Single(board.Columns, column => column.StateCode.Equals("Active", StringComparison.Ordinal));
            Assert.Contains(activeColumn.Tasks, task => task.Id.Equals(initial.SelectedTask.Id, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MoveTask_preserves_existing_active_pointer_when_other_live_tasks_exist()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);
            service.CreateTask(repository.RootPath, "Waiting task", null, string.Empty);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                service.MoveTask(repository.RootPath, initial.SelectedTask.Id, "Ready"));

            Assert.Contains("Move another task to Active", ex.Message, StringComparison.Ordinal);
            TaskBoardViewModel board = service.GetBoard(repository.RootPath, initial.SelectedTask.Id);
            Assert.NotNull(board.SelectedTask);
            Assert.Equal("Active", board.SelectedTask.StateCode);
        }
    }

    [Fact]
    public void ArchiveTask_moves_done_task_out_of_normal_groups_and_restore_returns_it()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);
            service.MoveTask(repository.RootPath, initial.SelectedTask.Id, "Done");

            TaskBoardTaskViewModel task = service.CreateTask(repository.RootPath, "Ship the thing", null, "done soon");

            service.MoveTask(repository.RootPath, task.Id, "Done");
            service.ArchiveTask(repository.RootPath, task.Id);

            TaskBoardViewModel archivedBoard = service.GetBoard(repository.RootPath, task.Id);
            TaskBoardColumnViewModel archiveColumn = Assert.Single(archivedBoard.Columns, column => column.IsArchive);
            Assert.Contains(archiveColumn.Tasks, archived => archived.Id.Equals(task.Id, StringComparison.Ordinal));
            Assert.DoesNotContain(
                archivedBoard.Columns.Where(column => !column.IsArchive).SelectMany(column => column.Tasks),
                visible => visible.Id.Equals(task.Id, StringComparison.Ordinal));

            service.RestoreTask(repository.RootPath, task.Id);

            TaskBoardViewModel restoredBoard = service.GetBoard(repository.RootPath, task.Id);
            TaskBoardColumnViewModel doneColumn = Assert.Single(restoredBoard.Columns, column => column.StateCode.Equals("Done", StringComparison.Ordinal));
            Assert.Contains(doneColumn.Tasks, restored => restored.Id.Equals(task.Id, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ArchiveTask_moves_non_active_task_out_of_normal_groups()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardTaskViewModel task = service.CreateTask(repository.RootPath, "Archive from ready", null, string.Empty);
            service.MoveTask(repository.RootPath, task.Id, "Ready");

            service.ArchiveTask(repository.RootPath, task.Id);

            TaskBoardViewModel archivedBoard = service.GetBoard(repository.RootPath, task.Id);
            Assert.NotNull(archivedBoard.SelectedTask);
            Assert.True(archivedBoard.SelectedTask.IsArchived);
            TaskBoardColumnViewModel archiveColumn = Assert.Single(archivedBoard.Columns, column => column.IsArchive);
            Assert.Contains(archiveColumn.Tasks, archived => archived.Id.Equals(task.Id, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ArchiveTask_rejects_active_task()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardViewModel initial = service.GetBoard(repository.RootPath, null);
            Assert.NotNull(initial.SelectedTask);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                service.ArchiveTask(repository.RootPath, initial.SelectedTask.Id));

            Assert.Contains("out of Active", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AddFile_rejects_absolute_or_parent_traversal_paths()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardTaskViewModel task = service.CreateTask(repository.RootPath, "Track file refs", null, string.Empty);

            Assert.Throws<ArgumentException>(() =>
                service.AddFile(repository.RootPath, task.Id, "C:/outside.txt", "bad", "input"));
            Assert.Throws<ArgumentException>(() =>
                service.AddFile(repository.RootPath, task.Id, "../outside.txt", "bad", "input"));

            service.AddFile(repository.RootPath, task.Id, "docs/task-note.md", "good", "context");
            TaskBoardViewModel board = service.GetBoard(repository.RootPath, task.Id);
            Assert.NotNull(board.SelectedTask);
            TaskBoardFileViewModel file = Assert.Single(board.SelectedTask.Files);
            Assert.Equal("docs/task-note.md", file.RelativePath);
        }
    }

    [Fact]
    public void EnsureCreated_preserves_existing_tasks()
    {
        using (TemporaryRepository repository = TemporaryRepository.Create())
        {
            WorkflowTaskBoardViewService service = new(CreateProvider());
            TaskBoardTaskViewModel created = service.CreateTask(repository.RootPath, "Keep this task", null, "durable");

            TaskBoardViewModel firstRead = service.GetBoard(repository.RootPath, created.Id);
            TaskBoardViewModel secondRead = service.GetBoard(repository.RootPath, created.Id);

            Assert.NotNull(firstRead.SelectedTask);
            Assert.NotNull(secondRead.SelectedTask);
            Assert.Equal(created.Id, secondRead.SelectedTask.Id);
            Assert.Equal("Keep this task", secondRead.SelectedTask.Name);
            Assert.Equal("durable", secondRead.SelectedTask.NotesMarkdown);
            Assert.Contains(
                secondRead.Columns.SelectMany(column => column.Tasks),
                task => task.Id.Equals(created.Id, StringComparison.Ordinal));
        }
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
}
