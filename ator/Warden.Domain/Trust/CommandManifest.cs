namespace Warden.Domain.Trust;

/// <summary>Um botão exposto pra Plateia: nome de exibição + argv exato + cwd, já resolvidos.</summary>
public sealed record ManifestCommand
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Argv { get; init; }
    public required string Cwd { get; init; }
    public bool Interactive { get; init; }
    public bool Destructive { get; init; }
}

/// <summary>
/// Superfície executável de um projeto, já resolvida a partir do `.toml` — a única coisa que a API
/// deve consultar pra listar OU executar um botão (nunca reler o arquivo de config no clique, ver
/// NEW_CONTEXT.md §10.4).
/// </summary>
public sealed record CommandManifest
{
    public required string ProjectId { get; init; }
    public required IReadOnlyList<ManifestCommand> Commands { get; init; }
}
