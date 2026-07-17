using System.Globalization;

namespace Warden.Domain.Vitals;

/// <summary>
/// CPU via `/proc/stat` (delta de jiffies entre duas leituras — mesma técnica que `top`/`htop` usam
/// por baixo dos panos) e RAM via `/proc/meminfo` (`MemAvailable`, não `MemFree` — accounting mais
/// realista de "memória disponível pra novas apps sem swap", mesma base que o `psutil.virtual_memory()`
/// usa no Linux).
/// </summary>
internal sealed class LinuxSystemVitalsSource : ISystemVitalsSource
{
    private ulong _lastIdle;
    private ulong _lastTotal;

    public void Prime() => (_lastIdle, _lastTotal) = ReadCpuTimes();

    public double SampleCpuPercent()
    {
        var (idle, total) = ReadCpuTimes();
        var idleDelta = idle - _lastIdle;
        var totalDelta = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;
        return totalDelta == 0 ? 0.0 : 100.0 * (1.0 - (double)idleDelta / totalDelta);
    }

    public (double UsedMb, double TotalMb) ReadMemory()
    {
        double? totalKb = null;
        double? availableKb = null;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ParseKb(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ParseKb(line);
            }
            if (totalKb is not null && availableKb is not null) break;
        }

        var totalMb = (totalKb ?? 0) / 1024.0;
        var availableMb = (availableKb ?? 0) / 1024.0;
        return (Math.Max(totalMb - availableMb, 0.0), totalMb);
    }

    private static double ParseKb(string line)
    {
        // formato: "MemTotal:       16384000 kB"
        var value = line.Split(':', 2)[1].Trim().Split(' ')[0];
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static (ulong Idle, ulong Total) ReadCpuTimes()
    {
        // primeira linha de /proc/stat: "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
        // (agregado de todos os núcleos). guest/guest_nice ficam de fora do total — já contados dentro
        // de user/nice pelo kernel, somar de novo dobraria a contagem (mesma convenção do htop).
        var line = File.ReadLines("/proc/stat").First();
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(f => ulong.Parse(f, CultureInfo.InvariantCulture)).ToArray();

        var user = fields[0];
        var nice = fields[1];
        var system = fields[2];
        var idle = fields[3];
        var iowait = fields.Length > 4 ? fields[4] : 0UL;
        var irq = fields.Length > 5 ? fields[5] : 0UL;
        var softirq = fields.Length > 6 ? fields[6] : 0UL;
        var steal = fields.Length > 7 ? fields[7] : 0UL;

        var idleAll = idle + iowait;
        var total = user + nice + system + idleAll + irq + softirq + steal;
        return (idleAll, total);
    }
}
