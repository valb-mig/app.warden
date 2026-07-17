namespace Warden.Domain.Adapters;

/// <summary>Interface comum de adapter: start/stop/status/logs (exec/ports/actions entram depois).</summary>
public interface IAdapter
{
    void Start();
    void Stop();
    ProcessStatus Status();
    IReadOnlyList<string> Logs(int tail = 100, string? service = null);

    /// <summary>Chamado quando o processo termina sozinho (não via <see cref="Stop"/>). No-op por padrão.</summary>
    void SetOnExit(Action<int> callback) { }

    /// <summary>Serviços individuais (ex: containers de um compose). Vazio = processo único.</summary>
    IReadOnlyList<string> Services() => [];
}
