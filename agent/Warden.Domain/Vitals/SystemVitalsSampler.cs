using System.Runtime.InteropServices;

namespace Warden.Domain.Vitals;

/// <summary>
/// Facade que amarra CPU/RAM (via <see cref="ISystemVitalsSource"/>, específico de plataforma) e
/// disco (via <see cref="DriveInfo"/>, já cross-platform nativo do .NET — sem P/Invoke necessário)
/// num snapshot só. Stateful só por causa do delta de CPU: uma instância por processo, `Prime()`
/// chamado uma vez no boot do Engine (mesmo papel do `prime_system_vitals()` do Python).
/// </summary>
public sealed class SystemVitalsSampler
{
    private static readonly string DiskRoot = OperatingSystem.IsWindows() ? "C:\\" : "/";

    private readonly ISystemVitalsSource _source = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new WindowsSystemVitalsSource()
        : new LinuxSystemVitalsSource();

    public void Prime() => _source.Prime();

    public SystemVitalsInfo Sample()
    {
        var cpuPercent = _source.SampleCpuPercent();
        var (usedMb, totalMb) = _source.ReadMemory();
        var memoryPercent = totalMb > 0 ? usedMb / totalMb * 100.0 : 0.0;

        var drive = new DriveInfo(DiskRoot);
        var totalBytes = (double)drive.TotalSize;
        var freeBytes = (double)drive.TotalFreeSpace;
        var usedBytes = Math.Max(totalBytes - freeBytes, 0.0);
        var diskPercent = totalBytes > 0 ? usedBytes / totalBytes * 100.0 : 0.0;

        return new SystemVitalsInfo
        {
            CpuPercent = cpuPercent,
            MemoryPercent = memoryPercent,
            MemoryUsedMb = usedMb,
            MemoryTotalMb = totalMb,
            DiskPercent = diskPercent,
            DiskUsedGb = usedBytes / (1024.0 * 1024.0 * 1024.0),
            DiskTotalGb = totalBytes / (1024.0 * 1024.0 * 1024.0),
        };
    }
}
