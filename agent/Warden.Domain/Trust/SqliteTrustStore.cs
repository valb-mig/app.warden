using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Warden.Domain.Trust;

/// <summary>
/// Persiste aprovações em `trusted_manifests` no mesmo `warden.db` usado pro histórico de eventos —
/// um arquivo só, sem servidor, mesma escolha de persistência do resto do projeto. Guarda o JSON do
/// manifesto aprovado (não só o digest) pra permitir diff "aprovado vs. atual" numa UI de Admin
/// futura, não só "hash bateu ou não bateu".
/// </summary>
public sealed class SqliteTrustStore : ITrustStore
{
    private readonly string _connectionString;

    public SqliteTrustStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public ApprovedManifest? GetApproved(string projectId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT digest, manifest_json, approved_at_utc FROM trusted_manifests WHERE project_id = $id";
        command.Parameters.AddWithValue("$id", projectId);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new ApprovedManifest
        {
            Digest = reader.GetString(0),
            ManifestJson = reader.GetString(1),
            ApprovedAtUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }

    public void Approve(string projectId, string digest, string manifestJson, DateTimeOffset approvedAtUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trusted_manifests (project_id, digest, manifest_json, approved_at_utc)
            VALUES ($id, $digest, $json, $approvedAt)
            ON CONFLICT(project_id) DO UPDATE SET
                digest = excluded.digest,
                manifest_json = excluded.manifest_json,
                approved_at_utc = excluded.approved_at_utc;
            """;
        command.Parameters.AddWithValue("$id", projectId);
        command.Parameters.AddWithValue("$digest", digest);
        command.Parameters.AddWithValue("$json", manifestJson);
        command.Parameters.AddWithValue("$approvedAt", approvedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS trusted_manifests (
                project_id TEXT PRIMARY KEY,
                digest TEXT NOT NULL,
                manifest_json TEXT NOT NULL,
                approved_at_utc TEXT NOT NULL
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
