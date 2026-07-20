using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Warden.Contracts.Projects;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta de `/projects/{id}/history` e `/projects/{id}/actions/audit` contra
/// um Kestrel/TestServer real (`Warden.Domain.Tests.SqliteEventStoreTests`/`EngineEventTests` já
/// cobrem a lógica a fundo) — aqui só valida o contrato HTTP: auth, 404, shape, fluxo real via start/stop.
/// </summary>
public sealed class HistoryApiTests : IAsyncLifetime
{
    private const string ProjectTomlTemplate = """
        id = "p"
        type = "raw"
        path = "{PROJECT_PATH}"

        [start]
        cmd = ["sleep", "30"]

        [[actions]]
        name = "wipe"
        cmd = ["echo", "wiped"]
        destructive = true
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly WardenAgentFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        var projectPath = Directory.CreateTempSubdirectory().FullName;
        _factory.WriteProject("p", ProjectTomlTemplate.Replace("{PROJECT_PATH}", projectPath));
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
    public async Task HistoryRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/projects/p/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HistoryForUnknownProjectIs404()
    {
        var response = await _client.GetAsync("/projects/nope/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HistoryEmptyBeforeAnyEvent()
    {
        var history = await _client.GetFromJsonAsync<List<HistoryEventDto>>("/projects/p/history", JsonOptions);

        Assert.Empty(history!);
    }

    [Fact]
    public async Task HistoryReflectsRealStartStop()
    {
        using var adminScope = _factory.Services.CreateScope();
        var engine = adminScope.ServiceProvider.GetRequiredService<Warden.Domain.Engine>();
        engine.Approve("p");

        await _client.PostAsync("/projects/p/start", null);
        await _client.PostAsync("/projects/p/stop", null);

        var history = await _client.GetFromJsonAsync<List<HistoryEventDto>>("/projects/p/history", JsonOptions);

        Assert.Equal(["stopped", "started"], history!.Select(h => h.Type));
    }

    [Fact]
    public async Task ActionAuditRecordsDestructiveAction()
    {
        using var adminScope = _factory.Services.CreateScope();
        var engine = adminScope.ServiceProvider.GetRequiredService<Warden.Domain.Engine>();
        engine.Approve("p");

        await _client.PostAsync("/projects/p/actions/wipe?confirm=true", null);

        var audit = await _client.GetFromJsonAsync<List<ActionAuditDto>>("/projects/p/actions/audit", JsonOptions);

        Assert.Single(audit!);
        Assert.Equal("wipe", audit![0].ActionName);
        Assert.True(audit[0].Confirmed);
    }
}
