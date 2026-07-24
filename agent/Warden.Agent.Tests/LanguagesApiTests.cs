using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Warden.Agent.Api;
using Warden.Contracts.Projects;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta do endpoint `/projects/{id}/languages` contra um Kestrel/TestServer
/// real. `Warden.Domain.Tests.LanguageDetectorTests` já cobre a lógica de detecção a fundo — aqui só
/// valida o contrato HTTP: auth, 404, shape do DTO.
/// </summary>
public sealed class LanguagesApiTests : IAsyncLifetime
{
    private const string ProjectTomlTemplate = """
        id = "p"
        type = "raw"
        path = "{PROJECT_PATH}"

        [start]
        cmd = ["true"]
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
        File.WriteAllText(Path.Combine(projectPath, "pyproject.toml"), "[project]\n");

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
    public async Task LanguagesRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/v1/projects/p/languages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LanguagesForUnknownProjectIs404()
    {
        var response = await _client.GetAsync("/v1/projects/nope/languages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LanguagesReflectsRealManifest()
    {
        var result = await _client.GetFromJsonAsync<LanguagesDto>("/v1/projects/p/languages", JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(["python"], result!.Languages);
    }
}
