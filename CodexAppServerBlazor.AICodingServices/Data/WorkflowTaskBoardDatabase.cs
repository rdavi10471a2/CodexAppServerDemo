using Microsoft.Data.Sqlite;

namespace CodexAppServerBlazor.AICodingServices.Data;

public sealed class WorkflowTaskBoardDatabase
{
    public const int SchemaVersion = 1;

    private readonly string databasePath;

    public WorkflowTaskBoardDatabase(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => databasePath;

    public SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();

        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "pragma journal_mode=wal; pragma foreign_keys=on;";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    public void EnsureCreated()
    {
        using (SqliteConnection connection = OpenConnection())
        {
            int persistedVersion = ReadUserVersion(connection);
            if (persistedVersion != SchemaVersion)
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    DropAllTables(connection, transaction);
                    CreateSchema(connection, transaction);
                    SeedLookups(connection, transaction);
                    transaction.Commit();
                }

                SetUserVersion(connection, SchemaVersion);
                return;
            }

            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                CreateSchema(connection, transaction);
                SeedLookups(connection, transaction);
                transaction.Commit();
            }
        }
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "pragma user_version;";
            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "pragma user_version = " + version + ";";
            command.ExecuteNonQuery();
        }
    }

    private static void DropAllTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        string[] tables =
        [
            "workflow_task_events",
            "workflow_task_files",
            "workflow_tasks",
            "workflow_task_event_types",
            "workflow_task_states"
        ];

        foreach (string table in tables)
        {
            Execute(connection, transaction, "drop table if exists " + table + ";");
        }
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
            create table if not exists workflow_task_states (
                code text primary key,
                name text not null,
                sort_order integer not null,
                is_terminal integer not null default 0
            );
            """);

        Execute(connection, transaction, """
            create table if not exists workflow_task_event_types (
                code text primary key,
                name text not null,
                sort_order integer not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists workflow_tasks (
                id text primary key,
                name text not null,
                state_code text not null references workflow_task_states(code),
                notes_markdown_path text null,
                created_at datetime not null,
                updated_at datetime not null,
                activated_at datetime null,
                completed_at datetime null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists workflow_task_files (
                id text primary key,
                task_id text not null references workflow_tasks(id) on delete cascade,
                relative_path text not null,
                intent text null,
                file_role text null,
                unique(task_id, relative_path)
            );
            """);

        Execute(connection, transaction, """
            create table if not exists workflow_task_events (
                id text primary key,
                task_id text not null references workflow_tasks(id) on delete cascade,
                event_type_code text not null references workflow_task_event_types(code),
                message text null,
                payload_json text null,
                created_at datetime not null
            );
            """);

        Execute(connection, transaction, "create unique index if not exists ux_workflow_tasks_single_active on workflow_tasks(state_code) where state_code = 'Active';");
        Execute(connection, transaction, "create index if not exists idx_workflow_tasks_state on workflow_tasks(state_code);");
        Execute(connection, transaction, "create index if not exists idx_workflow_task_files_task on workflow_task_files(task_id);");
        Execute(connection, transaction, "create index if not exists idx_workflow_task_events_task on workflow_task_events(task_id, created_at);");
    }

    private static void SeedLookups(SqliteConnection connection, SqliteTransaction transaction)
    {
        UpsertState(connection, transaction, "Proposed", "Proposed", 10, false);
        UpsertState(connection, transaction, "Ready", "Ready", 20, false);
        UpsertState(connection, transaction, "Active", "Active", 30, false);
        UpsertState(connection, transaction, "Review", "Review", 40, false);
        UpsertState(connection, transaction, "Done", "Done", 50, true);
        UpsertState(connection, transaction, "Blocked", "Blocked", 60, true);

        UpsertEventType(connection, transaction, "Created", "Created", 10);
        UpsertEventType(connection, transaction, "StateChanged", "State Changed", 20);
        UpsertEventType(connection, transaction, "NotesUpdated", "Notes Updated", 30);
        UpsertEventType(connection, transaction, "FileAdded", "File Added", 40);
        UpsertEventType(connection, transaction, "Comment", "Comment", 50);
    }

    private static void UpsertState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string code,
        string name,
        int sortOrder,
        bool isTerminal)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into workflow_task_states(code, name, sort_order, is_terminal)
                values ($code, $name, $sortOrder, $isTerminal)
                on conflict(code) do update set
                    name = excluded.name,
                    sort_order = excluded.sort_order,
                    is_terminal = excluded.is_terminal;
                """;
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$sortOrder", sortOrder);
            command.Parameters.AddWithValue("$isTerminal", isTerminal ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void UpsertEventType(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string code,
        string name,
        int sortOrder)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into workflow_task_event_types(code, name, sort_order)
                values ($code, $name, $sortOrder)
                on conflict(code) do update set
                    name = excluded.name,
                    sort_order = excluded.sort_order;
                """;
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$sortOrder", sortOrder);
            command.ExecuteNonQuery();
        }
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string commandText)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
    }
}
