using System.Net;

namespace Warden.Agent.Transport;

/// <summary>
/// Abstração sobre "onde a superfície do Console deve escutar" — hoje só existe
/// <see cref="TailscaleTransport"/>, mas isolar atrás de uma interface evita espalhar chamada
/// específica de Tailscale pelo resto do código de domínio (ver NEW_CONTEXT.md §7). Trocar por um
/// sistema de conexão multi-aparelho próprio no futuro não deveria tocar em execução de script,
/// notificação ou histórico.
/// </summary>
public interface IConsoleTransport
{
    /// <summary>
    /// Resolve o endpoint que o Console deve escutar. Lança <see cref="TailscaleUnavailableException"/>
    /// se não conseguir resolver com confiança — não existe fallback silencioso pra wildcard/loopback.
    /// </summary>
    IPEndPoint ResolveEndpoint(int port);
}

public sealed class TailscaleUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
