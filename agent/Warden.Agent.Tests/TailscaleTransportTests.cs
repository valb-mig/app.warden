using Warden.Agent.Transport;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Testa contra o `tailscale` real desta máquina (está na tailnet, ver `just boot`) — mesma
/// filosofia de validação real (não mockada) usada nos testes de adapter do Warden.Domain.
/// </summary>
public sealed class TailscaleTransportTests
{
    [Fact]
    public void ResolveEndpointReturnsRealTailscaleIpAndRequestedPort()
    {
        var transport = new TailscaleTransport();

        var endpoint = transport.ResolveEndpoint(8420);

        var bytes = endpoint.Address.GetAddressBytes();
        Assert.Equal(4, bytes.Length);
        Assert.Equal(100, bytes[0]);
        Assert.InRange(bytes[1], (byte)64, (byte)127); // faixa CGNAT do Tailscale
        Assert.Equal(8420, endpoint.Port);
    }

    [Fact]
    public void ResolveEndpointThrowsWhenCommandDoesNotExist()
    {
        var transport = new TailscaleTransport("this-binary-does-not-exist-warden-test");

        Assert.Throws<TailscaleUnavailableException>(() => transport.ResolveEndpoint(8420));
    }
}
