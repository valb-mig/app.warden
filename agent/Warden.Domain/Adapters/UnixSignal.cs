using System.Runtime.InteropServices;

namespace Warden.Domain.Adapters;

/// <summary>
/// SIGTERM cru via libc. `Process.Kill()` do .NET manda SIGKILL incondicional em Unix — não tem
/// como pedir desligamento gracioso (SIGTERM) pela API gerenciada, então falamos com o kernel direto.
/// Mesmo papel do `process.terminate()` do Python (que manda SIGTERM antes do `kill()` forçado).
/// </summary>
internal static class UnixSignal
{
    private const int Sigterm = 15;

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int NativeKill(int pid, int sig);

    public static bool TryTerminate(int pid)
    {
        try
        {
            return NativeKill(pid, Sigterm) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
