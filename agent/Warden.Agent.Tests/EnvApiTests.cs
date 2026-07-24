using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Warden.Domain;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Testa que variáveis de ambiente configuradas em ProjectConfig.Env chegam ao processo e que
/// o endpoint de action retorna o valor correto. O editor Admin de .env persiste o TOML e recarrega
/// — o round-trip de serialização é coberto por EnvScaffoldTests (Domain.Tests).
/// </summary>
public sealed class EnvApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly WardenAgentFactory _factory = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        var projectPath = Directory.CreateTempSubdirectory().FullName;
        _factory.WriteProject("env-proj", $"""
            id = "env-proj"
            type = "raw"
            path = "{projectPath}"
            [start]
            cmd = ["true"]
            [env]
            WARDEN_TEST_VAR = "hello-from-env"
            [[actions]]
            name = "read-env"
            cmd = ["printenv", "WARDEN_TEST_VAR"]
            """);

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _factory.ApiToken);

        _factory.Services.GetRequiredService<Engine>().Approve("env-proj");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Action_LeVariavelDeAmbiente_ConfiguradaNoEnv()
    {
        var response = await _client.PostAsync("/v1/projects/env-proj/actions/read-env", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ActionResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Contains("hello-from-env", result!.Output);
    }

    private sealed record ActionResult(int ExitCode, string Output);
}
