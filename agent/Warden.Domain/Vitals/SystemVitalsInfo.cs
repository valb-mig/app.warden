namespace Warden.Domain.Vitals;

/// <summary>CPU/RAM/disco da máquina como um todo — não por processo (isso é <see cref="Adapters.VitalsSampler"/>).</summary>
public sealed record SystemVitalsInfo
{
    public required double CpuPercent { get; init; }
    public required double MemoryPercent { get; init; }
    public required double MemoryUsedMb { get; init; }
    public required double MemoryTotalMb { get; init; }
    public required double DiskPercent { get; init; }
    public required double DiskUsedGb { get; init; }
    public required double DiskTotalGb { get; init; }
}
