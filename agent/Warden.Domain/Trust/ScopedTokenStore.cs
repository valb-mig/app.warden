using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Warden.Domain.Trust;

/// <summary>
/// Persiste tokens de escopo restrito em `scoped_tokens` no mesmo `warden.db`. Token bruto nunca
/// fica em disco — apenas SHA-256 (Base64 URL-safe). Emissão retorna o token cru uma única vez;
/// após isso só o hash é conhecido, seguindo o padrão de GitHub PATs e Anthropic API keys.
/// </summary>
public sealed class ScopedTokenStore
{
    private readonly string _connectionString;

    public ScopedTokenStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public sealed record ScopedTokenInfo(
        string Id,
        string Label,
        IReadOnlyList<string> AllowedProjectIds,
        DateTimeOffset CreatedAt,
        bool Revoked);

    /// <summary>
    /// Cria um scoped token e retorna o token bruto — único momento em que o valor cru existe
    /// fora da memória. Após retornar, apenas o hash fica persistido.
    /// </summary>
    public (string Id, string RawToken) Create(string label, IReadOnlyList<string> allowedProjectIds)
    {
        var id = Guid.NewGuid().ToString("N");
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = Hash(rawToken);
        var projectIdsJson = JsonSerializer.Serialize(allowedProjectIds);
        var createdAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO scoped_tokens (id, label, token_hash, allowed_project_ids, created_at)
            VALUES ($id, $label, $hash, $projectIds, $createdAt);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$hash", tokenHash);
        command.Parameters.AddWithValue("$projectIds", projectIdsJson);
        command.Parameters.AddWithValue("$createdAt", createdAt);
        command.ExecuteNonQuery();

        return (id, rawToken);
    }

    /// <summary>
    /// Valida se o token bruto tem acesso ao projeto informado. Retorna false se o token não
    /// existe, está revogado, ou o projectId não está no escopo permitido.
    /// </summary>
    public bool Validate(string rawToken, string projectId)
    {
        var hash = Hash(rawToken);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT allowed_project_ids FROM scoped_tokens
            WHERE token_hash = $hash AND revoked_at IS NULL;
            """;
        command.Parameters.AddWithValue("$hash", hash);

        var json = command.ExecuteScalar() as string;
        if (json is null) return false;

        var allowed = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return allowed.Contains(projectId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ScopedTokenInfo> List()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, label, allowed_project_ids, created_at, revoked_at FROM scoped_tokens ORDER BY created_at DESC;";

        using var reader = command.ExecuteReader();
        var result = new List<ScopedTokenInfo>();
        while (reader.Read())
        {
            var projectIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
            var createdAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var revoked = !reader.IsDBNull(4);
            result.Add(new ScopedTokenInfo(reader.GetString(0), reader.GetString(1), projectIds, createdAt, revoked));
        }
        return result;
    }

    public void Revoke(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE scoped_tokens SET revoked_at = $revokedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$revokedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS scoped_tokens (
                id TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                token_hash TEXT NOT NULL UNIQUE,
                allowed_project_ids TEXT NOT NULL,
                created_at TEXT NOT NULL,
                revoked_at TEXT
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

    private static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
