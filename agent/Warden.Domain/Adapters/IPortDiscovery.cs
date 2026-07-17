using System.Runtime.InteropServices;

namespace Warden.Domain.Adapters;

/// <summary>Portas TCP em LISTEN abertas por um PID — equivalente ao `psutil.Process.net_connections()` do Python.</summary>
public interface IPortDiscovery
{
    IReadOnlyList<int> ListeningPorts(int pid);
}

public static class PortDiscovery
{
    private static readonly IPortDiscovery Instance = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new WindowsPortDiscovery()
        : new LinuxPortDiscovery();

    /// <summary>
    /// No Linux é a implementação real (parse de /proc); em qualquer outro SO não-Windows (ex: macOS,
    /// fora do escopo do Warden — ver NEW_CONTEXT.md §1) degrada pra lista vazia em vez de lançar,
    /// já que /proc simplesmente não existe lá.
    /// </summary>
    public static IPortDiscovery ForCurrentPlatform() => Instance;
}
