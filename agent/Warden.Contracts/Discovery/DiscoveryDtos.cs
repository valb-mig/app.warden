namespace Warden.Contracts.Discovery;

/// <summary>
/// DTOs de descoberta/sincronização de projeto (`scan_paths`/`discover`/`browse`/`discover/preview`/
/// `discover/apply`, TODO.md decisão #17) — mesmo contrato do `web/src/lib/api.ts`. Vivem em
/// Warden.Contracts pelo mesmo motivo de <c>Warden.Contracts.Projects</c>: Warden.Admin fala essas
/// rotas direto pelo socket unix (não são filtradas pra unix-socket-only), então Agent e Admin
/// compartilham o shape sem duplicar.
/// </summary>
public sealed record DiscoveredProjectDto
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Type { get; init; }
}

public sealed record DiscoverResultDto
{
    public required IReadOnlyList<DiscoveredProjectDto> Projects { get; init; }
}

public sealed record BrowseEntryDto
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

public sealed record BrowseResultDto
{
    public required string Path { get; init; }
    public string? Parent { get; init; }
    public required IReadOnlyList<BrowseEntryDto> Entries { get; init; }
}

public sealed record ScanPathsDto
{
    public required IReadOnlyList<string> ScanPaths { get; init; }
}

public sealed record StartConfigDto
{
    public required IReadOnlyList<string> Cmd { get; init; }
    public string? Cwd { get; init; }
    public bool CaptureStdout { get; init; }
}

public sealed record NotifyConfigDto
{
    public bool OnError { get; init; }
    public bool OnFinished { get; init; }
    public bool OnGitBehind { get; init; }
}

public sealed record GitWatchConfigDto
{
    public bool Watch { get; init; }
    public double Interval { get; init; } = 300.0;
    public string Remote { get; init; } = "origin";
}

public sealed record LogSourceDto
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Path { get; init; }
    public string? Service { get; init; }
    public IReadOnlyList<string> ErrorPatterns { get; init; } = [];
}

public sealed record ActionConfigDto
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Cmd { get; init; } = [];
    public bool Interactive { get; init; }
    public bool Destructive { get; init; }
}

public sealed record ProjectConfigDto
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public string? Group { get; init; }
    public required string Path { get; init; }
    public required string Type { get; init; }
    public string? ComposeFile { get; init; }
    public IReadOnlyList<string> ComposeServices { get; init; } = [];
    public StartConfigDto? Start { get; init; }
    public NotifyConfigDto Notify { get; init; } = new();
    public GitWatchConfigDto Git { get; init; } = new();
    public IReadOnlyList<LogSourceDto> LogSources { get; init; } = [];
    public IReadOnlyList<ActionConfigDto> Actions { get; init; } = [];
}

public sealed record ConfigPreviewDto
{
    public required ProjectConfigDto Config { get; init; }
    public required string Toml { get; init; }
}
