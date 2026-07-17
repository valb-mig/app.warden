using System.Runtime.InteropServices;

namespace Warden.Domain.Vitals;

/// <summary>
/// CPU via `GetSystemTimes` (kernel32) e RAM via `GlobalMemoryStatusEx` — APIs estáveis desde Windows
/// XP, mesmo mecanismo que o Gerenciador de Tarefas usa.
///
/// ATENÇÃO: implementado com base na documentação da API, mas não validado numa máquina Windows real
/// nesta sessão (ambiente de dev é Linux/Arch) — mesma ressalva já registrada em
/// <see cref="Adapters.WindowsPortDiscovery"/> (NEW_CONTEXT.md §12 fase 2). Validar antes de confiar
/// cegamente em produção Windows.
/// </summary>
internal sealed class WindowsSystemVitalsSource : ISystemVitalsSource
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
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0.0, 0.0);

        var totalMb = status.TotalPhys / (1024.0 * 1024.0);
        var availMb = status.AvailPhys / (1024.0 * 1024.0);
        return (Math.Max(totalMb - availMb, 0.0), totalMb);
    }

    private static (ulong Idle, ulong Total) ReadCpuTimes()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        var idleTicks = ToUlong(idle);
        var totalTicks = ToUlong(kernel) + ToUlong(user); // kernelTime já inclui o idleTime
        return (idleTicks, totalTicks);
    }

    private static ulong ToUlong(FileTime ft) => ((ulong)ft.DwHighDateTime << 32) | ft.DwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
