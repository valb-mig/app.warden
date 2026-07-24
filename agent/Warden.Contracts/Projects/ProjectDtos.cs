namespace Warden.Contracts.Projects;

/// <summary>
/// DTOs da API REST de projeto/sistema — casing JSON é snake_case (configurado nos consumidores),
/// mesmo contrato do FastAPI atual. Vivem em Warden.Contracts (não em Warden.Agent) porque
/// Warden.Admin também fala esses tipos diretamente com o Agent pelo socket unix (as rotas
/// `/projects`/`/system` não são filtradas pra unix-socket-only, só `/admin` é — ver Program.cs),
/// então Agent e Admin compartilham o mesmo shape sem duplicar registros.
/// </summary>
public sealed record ProjectDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Group { get; init; }
}

public sealed record StatusDto
{
    public required bool Running { get; init; }
    public int? Pid { get; init; }
    public IReadOnlyList<int> Ports { get; init; } = [];
    public double? UptimeSeconds { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryMb { get; init; }
    /// <summary>Valores possíveis: "approved", "pending_review", "never_approved".</summary>
    public required string TrustStatus { get; init; }
}

public sealed record LogsDto
{
    public required IReadOnlyList<string> Lines { get; init; }
}

public sealed record ServicesDto
{
    public required IReadOnlyList<string> Services { get; init; }
    public IReadOnlyList<string> ErrorPatterns { get; init; } = [];
}

public sealed record LanguagesDto
{
    public required IReadOnlyList<string> Languages { get; init; }
}

public sealed record ActionDto
{
    public required string Name { get; init; }
    public required bool Interactive { get; init; }
    public required bool Destructive { get; init; }

    /// <summary>Extensão em relação ao contrato Python: reflete o trust gate (NEW_CONTEXT.md §10.3) — false = botão deve aparecer desabilitado.</summary>
    public required bool Approved { get; init; }
}

public sealed record ActionResultDto
{
    public required int ExitCode { get; init; }
    public required string Output { get; init; }
}

public sealed record GitCommitDto
{
    public required string Hash { get; init; }
    public required string Subject { get; init; }
    public required string Author { get; init; }
    public required string Relative { get; init; }
}

public sealed record GitInfoDto
{
    public required string Branch { get; init; }
    public required bool Dirty { get; init; }
    public required int DirtyCount { get; init; }
    public int? Ahead { get; init; }
    public int? Behind { get; init; }
    public required bool HasRemote { get; init; }
    public GitCommitDto? LastCommit { get; init; }
}

public sealed record GitCommandResultDto
{
    public required bool Ok { get; init; }
    public required int ExitCode { get; init; }
    public required string Output { get; init; }
    public required bool Refused { get; init; }
}

public sealed record HistoryEventDto
{
    public required string ProjectId { get; init; }
    public required string Type { get; init; }
    public required string Message { get; init; }
    public required string CreatedAt { get; init; }
}

public sealed record ActionAuditDto
{
    public required string ProjectId { get; init; }
    public required string ActionName { get; init; }
    public required IReadOnlyList<string> Cmd { get; init; }
    public required bool Confirmed { get; init; }
    public required int ExitCode { get; init; }
    public required string CreatedAt { get; init; }
}

public sealed record SystemVitalsDto
{
    public required double CpuPercent { get; init; }
    public required double MemoryPercent { get; init; }
    public required double MemoryUsedMb { get; init; }
    public required double MemoryTotalMb { get; init; }
    public required double DiskPercent { get; init; }
    public required double DiskUsedGb { get; init; }
    public required double DiskTotalGb { get; init; }
}
