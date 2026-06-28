using Microsoft.Data.Sqlite;

namespace CodexAppServerBlazor.AICodingServices.Data;

public sealed class WorkflowTaskBoardRepository
{
    private readonly WorkflowTaskBoardDatabase database;
    private readonly string taskMemoryRoot;

    public WorkflowTaskBoardRepository(string databasePath, string taskMemoryRoot)
    {
        database = new WorkflowTaskBoardDatabase(databasePath);
        this.taskMemoryRoot = Path.GetFullPath(taskMemoryRoot);
    }

    public string DatabasePath => database.DatabasePath;

    public string TaskMemoryRoot => taskMemoryRoot;

    public void EnsureCreated()
    {
        Directory.CreateDirectory(taskMemoryRoot);
        database.EnsureCreated();
    }

    public WorkflowTaskBoardSnapshot LoadSnapshot()
    {
        EnsureCreated();
        using (SqliteConnection connection = database.OpenConnection())
        {
            return new WorkflowTaskBoardSnapshot(
                LoadStates(connection),
                LoadEventTypes(connection),
                LoadTasks(connection),
                LoadFiles(connection),
                LoadEvents(connection));
        }
    }

    public WorkflowTaskRow CreateTask(string name, string? notesMarkdown)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Task name is required.", nameof(name));
        }

        EnsureCreated();
        string taskId = CreateId();
        DateTime now = DateTime.Now;
        string? notesPath = null;
        if (!string.IsNullOrWhiteSpace(notesMarkdown))
        {
            notesPath = GetTaskNotesPath(taskId);
            File.WriteAllText(notesPath, notesMarkdown);
        }

        using (SqliteConnection connection = database.OpenConnection())
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    insert into workflow_tasks(id, name, state_code, notes_markdown_path, created_at, updated_at)
                    values ($id, $name, 'Proposed', $notesMarkdownPath, $createdAt, $updatedAt);
                    """;
                command.Parameters.AddWithValue("$id", taskId);
                command.Parameters.AddWithValue("$name", name.Trim());
                AddNullable(command, "$notesMarkdownPath", notesPath);
                command.Parameters.AddWithValue("$createdAt", now);
                command.Parameters.AddWithValue("$updatedAt", now);
                command.ExecuteNonQuery();
            }

            InsertEvent(connection, taskId, "Created", "Task created.", null, now);
            return LoadTask(connection, taskId);
        }
    }

    public WorkflowTaskRow MoveTask(string taskId, string stateCode)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("Task id is required.", nameof(taskId));
        }

        if (string.IsNullOrWhiteSpace(stateCode))
        {
            throw new ArgumentException("Task state is required.", nameof(stateCode));
        }

        EnsureCreated();
        using (SqliteConnection connection = database.OpenConnection())
        {
            EnsureStateExists(connection, stateCode);
            DateTime now = DateTime.Now;
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    update workflow_tasks
                    set state_code = $stateCode,
                        updated_at = $updatedAt,
                        activated_at = case when $stateCode = 'Active' then coalesce(activated_at, $updatedAt) else activated_at end,
                        completed_at = case when $stateCode in ('Done', 'Blocked') then coalesce(completed_at, $updatedAt) else null end
                    where id = $id;
                    """;
                command.Parameters.AddWithValue("$id", taskId);
                command.Parameters.AddWithValue("$stateCode", stateCode);
                command.Parameters.AddWithValue("$updatedAt", now);
                try
                {
                    int updated = command.ExecuteNonQuery();
                    if (updated == 0)
                    {
                        throw new InvalidOperationException("Task was not found: " + taskId);
                    }
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && stateCode.Equals("Active", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Only one task can be Active.", ex);
                }
            }

            InsertEvent(connection, taskId, "StateChanged", "Task moved to " + stateCode + ".", null, now);
            return LoadTask(connection, taskId);
        }
    }

    public WorkflowTaskRow UpdateNotes(string taskId, string notesMarkdown)
    {
        EnsureCreated();
        string notesPath = GetTaskNotesPath(taskId);
        File.WriteAllText(notesPath, notesMarkdown ?? string.Empty);
        DateTime now = DateTime.Now;

        using (SqliteConnection connection = database.OpenConnection())
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    update workflow_tasks
                    set notes_markdown_path = $notesMarkdownPath,
                        updated_at = $updatedAt
                    where id = $id;
                    """;
                command.Parameters.AddWithValue("$id", taskId);
                command.Parameters.AddWithValue("$notesMarkdownPath", notesPath);
                command.Parameters.AddWithValue("$updatedAt", now);
                int updated = command.ExecuteNonQuery();
                if (updated == 0)
                {
                    throw new InvalidOperationException("Task was not found: " + taskId);
                }
            }

            InsertEvent(connection, taskId, "NotesUpdated", "Task notes updated.", null, now);
            return LoadTask(connection, taskId);
        }
    }

    public WorkflowTaskFileRow AddFile(string taskId, string relativePath, string? intent, string? fileRole)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Task file path is required.", nameof(relativePath));
        }

        EnsureCreated();
        string id = CreateId();
        DateTime now = DateTime.Now;
        using (SqliteConnection connection = database.OpenConnection())
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    insert into workflow_task_files(id, task_id, relative_path, intent, file_role)
                    values ($id, $taskId, $relativePath, $intent, $fileRole)
                    on conflict(task_id, relative_path) do update set
                        intent = excluded.intent,
                        file_role = excluded.file_role;
                    """;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$taskId", taskId);
                command.Parameters.AddWithValue("$relativePath", NormalizeRelativePath(relativePath));
                AddNullable(command, "$intent", intent);
                AddNullable(command, "$fileRole", fileRole);
                command.ExecuteNonQuery();
            }

            TouchTask(connection, taskId, now);
            InsertEvent(connection, taskId, "FileAdded", "Task file added: " + NormalizeRelativePath(relativePath), null, now);
            return LoadFiles(connection).First(file =>
                file.TaskId.Equals(taskId, StringComparison.Ordinal)
                && file.RelativePath.Equals(NormalizeRelativePath(relativePath), StringComparison.OrdinalIgnoreCase));
        }
    }

    public WorkflowTaskEventRow AddComment(string taskId, string message)
    {
        EnsureCreated();
        DateTime now = DateTime.Now;
        using (SqliteConnection connection = database.OpenConnection())
        {
            WorkflowTaskEventRow row = InsertEvent(connection, taskId, "Comment", message, null, now);
            TouchTask(connection, taskId, now);
            return row;
        }
    }

    public string ReadNotes(string? notesMarkdownPath)
    {
        if (string.IsNullOrWhiteSpace(notesMarkdownPath) || !File.Exists(notesMarkdownPath))
        {
            return string.Empty;
        }

        return File.ReadAllText(notesMarkdownPath);
    }

    private string GetTaskNotesPath(string taskId)
    {
        Directory.CreateDirectory(taskMemoryRoot);
        return Path.Combine(taskMemoryRoot, taskId + ".md");
    }

    private static IReadOnlyList<WorkflowTaskStateRow> LoadStates(SqliteConnection connection)
    {
        List<WorkflowTaskStateRow> rows = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select code, name, sort_order, is_terminal from workflow_task_states order by sort_order;";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new WorkflowTaskStateRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetInt32(2),
                        reader.GetInt32(3) != 0));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<WorkflowTaskEventTypeRow> LoadEventTypes(SqliteConnection connection)
    {
        List<WorkflowTaskEventTypeRow> rows = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select code, name, sort_order from workflow_task_event_types order by sort_order;";
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new WorkflowTaskEventTypeRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetInt32(2)));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<WorkflowTaskRow> LoadTasks(SqliteConnection connection)
    {
        List<WorkflowTaskRow> rows = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                select t.id, t.name, t.state_code, s.name, t.notes_markdown_path,
                       t.created_at, t.updated_at, t.activated_at, t.completed_at
                from workflow_tasks t
                join workflow_task_states s on s.code = t.state_code
                order by s.sort_order, t.updated_at desc, t.created_at desc;
                """;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(ReadTask(reader));
                }
            }
        }

        return rows;
    }

    private static WorkflowTaskRow LoadTask(SqliteConnection connection, string taskId)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                select t.id, t.name, t.state_code, s.name, t.notes_markdown_path,
                       t.created_at, t.updated_at, t.activated_at, t.completed_at
                from workflow_tasks t
                join workflow_task_states s on s.code = t.state_code
                where t.id = $id;
                """;
            command.Parameters.AddWithValue("$id", taskId);
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return ReadTask(reader);
                }
            }
        }

        throw new InvalidOperationException("Task was not found: " + taskId);
    }

    private static WorkflowTaskRow ReadTask(SqliteDataReader reader)
    {
        return new WorkflowTaskRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetDateTime(5),
            reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8));
    }

    private static IReadOnlyList<WorkflowTaskFileRow> LoadFiles(SqliteConnection connection)
    {
        List<WorkflowTaskFileRow> rows = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                select id, task_id, relative_path, intent, file_role
                from workflow_task_files
                order by relative_path;
                """;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new WorkflowTaskFileRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<WorkflowTaskEventRow> LoadEvents(SqliteConnection connection)
    {
        List<WorkflowTaskEventRow> rows = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                select e.id, e.task_id, e.event_type_code, et.name, e.message, e.payload_json, e.created_at
                from workflow_task_events e
                join workflow_task_event_types et on et.code = e.event_type_code
                order by e.created_at desc;
                """;
            using (SqliteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new WorkflowTaskEventRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.GetDateTime(6)));
                }
            }
        }

        return rows;
    }

    private static WorkflowTaskEventRow InsertEvent(
        SqliteConnection connection,
        string taskId,
        string eventTypeCode,
        string? message,
        string? payloadJson,
        DateTime createdAt)
    {
        string id = CreateId();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                insert into workflow_task_events(id, task_id, event_type_code, message, payload_json, created_at)
                values ($id, $taskId, $eventTypeCode, $message, $payloadJson, $createdAt);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$eventTypeCode", eventTypeCode);
            AddNullable(command, "$message", message);
            AddNullable(command, "$payloadJson", payloadJson);
            command.Parameters.AddWithValue("$createdAt", createdAt);
            command.ExecuteNonQuery();
        }

        return new WorkflowTaskEventRow(id, taskId, eventTypeCode, eventTypeCode, message, payloadJson, createdAt);
    }

    private static void TouchTask(SqliteConnection connection, string taskId, DateTime now)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "update workflow_tasks set updated_at = $updatedAt where id = $id;";
            command.Parameters.AddWithValue("$id", taskId);
            command.Parameters.AddWithValue("$updatedAt", now);
            int updated = command.ExecuteNonQuery();
            if (updated == 0)
            {
                throw new InvalidOperationException("Task was not found: " + taskId);
            }
        }
    }

    private static void EnsureStateExists(SqliteConnection connection, string stateCode)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "select count(*) from workflow_task_states where code = $stateCode;";
            command.Parameters.AddWithValue("$stateCode", stateCode);
            long count = Convert.ToInt64(command.ExecuteScalar() ?? 0);
            if (count == 0)
            {
                throw new InvalidOperationException("Task state was not found: " + stateCode);
            }
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Trim().Replace('\\', '/');
    }

    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static void AddNullable(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);
    }
}
