namespace Warden.Agent.Api;

/// <summary>DTOs da API REST — casing JSON é snake_case (configurado no Program.cs), mesmo contrato do FastAPI atual.</summary>
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
