using System.Collections.Concurrent;
using System.Diagnostics;

namespace Warden.Agent.Hubs;

/// <summary>
/// Mapeia connectionId do Console (SignalR) → processos filhos de streaming abertos por essa conexão
/// (ex: `docker compose logs -f`/exec interativo — ver NEW_CONTEXT.md §10.5). Hoje é só a infra: o
/// tailing de log atual lê do <c>RingBuffer</c> já mantido pelo adapter, não abre processo por
/// conexão; o primeiro produtor real chega na fase 8 (paridade de streaming/exec). O <c>LogsHub</c>
/// já chama <see cref="KillAll"/> em <c>OnDisconnectedAsync</c>, então a limpeza funciona assim que
/// algo passar a registrar processo aqui.
/// </summary>
public sealed class ChildProcessRegistry
{
    private readonly ConcurrentDictionary<string, List<Process>> _byConnection = new();
    private readonly Lock _lock = new();

    public void Register(string connectionId, Process process)
    {
        lock (_lock)
        {
            _byConnection.GetOrAdd(connectionId, _ => []).Add(process);
        }
    }

    public void KillAll(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var processes)) return;

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // já morreu entre o HasExited e o Kill — corrida benigna
            }
        }
    }
}
