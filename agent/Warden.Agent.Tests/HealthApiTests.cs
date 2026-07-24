using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Warden.Agent.Tests;

public sealed class HealthApiTests : IAsyncLifetime
{
    private readonly WardenAgentFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_factory.ConfigDir, recursive: true);
    }

    [Fact]
    public async Task Health_ReturnsOkWithoutToken()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsExpectedShape()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("uptime_seconds").GetInt64() >= 0);
    }
}
