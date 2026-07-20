using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Warden.Agent.Api;
using Warden.Contracts.Projects;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta do endpoint `/system/vitals` contra um Kestrel/TestServer real —
/// máquina real por baixo (Linux), sem mock. `Warden.Domain.Tests.SystemVitalsSamplerTests` já cobre
/// ranges/plausibilidade a fundo; aqui só valida o contrato HTTP: auth, shape do DTO.
/// </summary>
public sealed class SystemVitalsApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly WardenAgentFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _factory.ApiToken);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SystemVitalsRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/system/vitals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SystemVitalsReturnsPlausibleShape()
    {
        var result = await _client.GetFromJsonAsync<SystemVitalsDto>("/system/vitals", JsonOptions);

        Assert.NotNull(result);
        Assert.InRange(result!.MemoryPercent, 0.0, 100.0);
        Assert.True(result.MemoryTotalMb > 0);
        Assert.InRange(result.DiskPercent, 0.0, 100.0);
        Assert.True(result.DiskTotalGb > 0);
    }
}
