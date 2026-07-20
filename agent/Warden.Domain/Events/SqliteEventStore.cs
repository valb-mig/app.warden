using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Warden.Domain.Events;

/// <summary>
/// Persiste em `events`/`action_audit` no mesmo `warden.db` que `SqliteTrustStore` já usa — um
/// arquivo só, sem servidor. Mirror de `store.py`. Uma `SqliteConnection` nova por chamada (mesmo
/// padrão do `SqliteTrustStore`) em vez de uma conexão compartilhada de longa duração — evita
/// precisar de lock manual pra concorrência entre threads (callback de exit de processo, thread do
/// `GitWatcher`, request HTTP), o SQLite já serializa o acesso ao arquivo por baixo.
/// </summary>
public sealed class SqliteEventStore : IEventStore
{
    private readonly string _connectionString;

    public SqliteEventStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public void Record(Event @event)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events (project_id, type, message) VALUES ($projectId, $type, $message)";
        command.Parameters.AddWithValue("$projectId", @event.ProjectId);
        command.Parameters.AddWithValue("$type", @event.Type.ToWireString());
        command.Parameters.AddWithValue("$message", @event.Message);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<HistoryEntry> History(string projectId, int limit = 50)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT project_id, type, message, created_at FROM events
            WHERE project_id = $projectId ORDER BY id DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var results = new List<HistoryEntry>();
        while (reader.Read())
        {
            results.Add(new HistoryEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        return results;
    }

    public void RecordAction(string projectId, string actionName, IReadOnlyList<string> cmd, bool confirmed, int exitCode)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO action_audit (project_id, action_name, cmd, confirmed, exit_code)
            VALUES ($projectId, $actionName, $cmd, $confirmed, $exitCode)
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$actionName", actionName);
        command.Parameters.AddWithValue("$cmd", JsonSerializer.Serialize(cmd));
        command.Parameters.AddWithValue("$confirmed", confirmed ? 1 : 0);
        command.Parameters.AddWithValue("$exitCode", exitCode);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ActionAuditEntry> ActionAudit(string projectId, int limit = 50)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT project_id, action_name, cmd, confirmed, exit_code, created_at FROM action_audit
            WHERE project_id = $projectId ORDER BY id DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var results = new List<ActionAuditEntry>();
        while (reader.Read())
        {
            var cmd = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
            results.Add(new ActionAuditEntry(
                reader.GetString(0),
                reader.GetString(1),
                cmd,
                reader.GetInt32(3) != 0,
                reader.GetInt32(4),
                reader.GetString(5)));
        }
        return results;
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                type TEXT NOT NULL,
                message TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            );
            CREATE TABLE IF NOT EXISTS action_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                action_name TEXT NOT NULL,
                cmd TEXT NOT NULL,
                confirmed INTEGER NOT NULL,
                exit_code INTEGER NOT NULL,
                created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
