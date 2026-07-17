using System.Net;

namespace Warden.Agent.Transport;

/// <summary>
/// Defesa em profundidade (cinto e suspensório) além do <see cref="TailscaleTransport"/>: depois do
/// Kestrel subir, confirma que o bind real bate com o esperado. A defesa primária já é nunca dar ao
/// Kestrel a chance de escolher (endpoint explícito, nunca `UseUrls`/wildcard) — isso aqui pega
/// regressão futura tipo alguém reintroduzindo `UseUrls` num merge. Ver NEW_CONTEXT.md §10.2.
/// </summary>
public static class BindGuard
{
    public static void AssertSingleExpectedAddress(IReadOnlyCollection<string> boundAddresses, IPEndPoint expected)
    {
        if (boundAddresses.Count != 1)
        {
            throw new BindAssertionException(
                $"esperava exatamente 1 endereço de bind pro Console, achou {boundAddresses.Count}: " +
                string.Join(", ", boundAddresses));
        }

        var boundAddress = boundAddresses.Single();
        var uri = new Uri(boundAddress);
        if (!IPAddress.TryParse(uri.Host, out var boundIp) || !boundIp.Equals(expected.Address) || uri.Port != expected.Port)
        {
            throw new BindAssertionException(
                $"bind divergente do esperado — esperado {expected}, encontrado {boundAddress}");
        }
    }
}

public sealed class BindAssertionException(string message) : Exception(message);
