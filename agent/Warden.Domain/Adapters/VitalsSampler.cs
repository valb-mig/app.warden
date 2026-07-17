using System.Diagnostics;

namespace Warden.Domain.Adapters;

/// <summary>
/// CPU/RAM por PID via <see cref="Process"/> — cacheia o processo pra ter um baseline real de CPU,
/// já que a primeira leitura não tem delta (mesmo problema que o `psutil.cpu_percent()` resolve
/// "primando" a leitura). CPU não é normalizado por `Environment.ProcessorCount` — mesma convenção
/// do psutil, onde um processo saturando 2 núcleos reporta ~200%, não 100%.
/// </summary>
public sealed class VitalsSampler
{
    private int? _pid;
    private Process? _process;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleTimeUtc;

    public (double? CpuPercent, double? MemoryMb) Sample(int pid)
    {
        if (_process is null || _pid != pid)
        {
            if (!TryAttach(pid)) return (null, null);
            return (null, null); // prime: primeira leitura não tem baseline, descarta
        }

        try
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var cpuTime = _process.TotalProcessorTime;

            var wallDeltaMs = (now - _lastSampleTimeUtc).TotalMilliseconds;
            var cpuDeltaMs = (cpuTime - _lastCpuTime).TotalMilliseconds;
            var cpuPercent = wallDeltaMs > 0 ? cpuDeltaMs / wallDeltaMs * 100.0 : 0.0;
            var memoryMb = _process.WorkingSet64 / (1024.0 * 1024.0);

            _lastCpuTime = cpuTime;
            _lastSampleTimeUtc = now;

            return (cpuPercent, memoryMb);
        }
        catch (InvalidOperationException)
        {
            _process = null;
            _pid = null;
            return (null, null);
        }
    }

    private bool TryAttach(int pid)
    {
        try
        {
            _process = Process.GetProcessById(pid);
            _process.Refresh();
            _lastCpuTime = _process.TotalProcessorTime;
            _lastSampleTimeUtc = DateTime.UtcNow;
            _pid = pid;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
