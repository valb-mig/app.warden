namespace Warden.Domain.Adapters;

public sealed record ProcessStatus
{
    public required bool Running { get; init; }
    public int? Pid { get; init; }
    public IReadOnlyList<int> Ports { get; init; } = [];
    public double? UptimeSeconds { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryMb { get; init; }
}
