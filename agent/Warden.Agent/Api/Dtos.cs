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
