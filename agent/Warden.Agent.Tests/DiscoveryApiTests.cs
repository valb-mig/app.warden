using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Warden.Contracts.Discovery;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta das rotas de descoberta/sincronização contra um Kestrel/TestServer
/// real (`Warden.Domain.Tests.ScaffoldTests`/`ProjectDiscoveryTests` já cobrem a lógica a fundo) —
/// aqui só valida o contrato HTTP: auth e o fluxo completo scan-path → discover → preview → apply,
/// escrevendo/lendo arquivos reais em disco, nada mockado.
/// </summary>
public sealed class DiscoveryApiTests : IAsyncLifetime
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
    public async Task ScanPathsRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/v1/scan-paths");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddAndRemoveScanPathRoundTrips()
    {
        var scanRoot = Directory.CreateTempSubdirectory().FullName;

        var added = await (await _client.PostAsJsonAsync("/v1/scan-paths", new { path = scanRoot }, JsonOptions))
            .Content.ReadFromJsonAsync<ScanPathsDto>(JsonOptions);
        Assert.Contains(scanRoot, added!.ScanPaths);

        var listed = await _client.GetFromJsonAsync<ScanPathsDto>("/v1/scan-paths", JsonOptions);
        Assert.Contains(scanRoot, listed!.ScanPaths);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/scan-paths")
        {
            Content = JsonContent.Create(new { path = scanRoot }, options: JsonOptions),
        };
        var removed = await (await _client.SendAsync(request)).Content.ReadFromJsonAsync<ScanPathsDto>(JsonOptions);
        Assert.DoesNotContain(scanRoot, removed!.ScanPaths);
    }

    [Fact]
    public async Task AddScanPathRejectsNonDirectory()
    {
        var response = await _client.PostAsJsonAsync("/v1/scan-paths", new { path = "/nope/definitely/not/real" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BrowseListsSubdirectories()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        var result = await _client.GetFromJsonAsync<BrowseResultDto>($"/v1/browse?path={Uri.EscapeDataString(root)}", JsonOptions);

        Assert.NotNull(result);
        Assert.Contains(result!.Entries, e => e.Name == "sub");
    }

    [Fact]
    public async Task DiscoverThenPreviewThenApplyRegistersRealProject()
    {
        var scanRoot = Directory.CreateTempSubdirectory().FullName;
        var newProjectDir = Path.Combine(scanRoot, "found-project");
        Directory.CreateDirectory(newProjectDir);
        File.WriteAllText(Path.Combine(newProjectDir, "manage.py"), "");

        await _client.PostAsJsonAsync("/v1/scan-paths", new { path = scanRoot }, JsonOptions);

        var discovered = await _client.GetFromJsonAsync<DiscoverResultDto>("/v1/discover", JsonOptions);
        var found = Assert.Single(discovered!.Projects, p => p.Name == "found-project");
        Assert.Equal("python", found.Type);

        var preview = await (await _client.PostAsJsonAsync("/v1/discover/preview", new { path = found.Path, id = (string?)null }, JsonOptions))
            .Content.ReadFromJsonAsync<ConfigPreviewDto>(JsonOptions);
        Assert.Equal("found-project", preview!.Config.Id);
        Assert.Equal(["python", "manage.py", "runserver"], preview.Config.Start!.Cmd);

        var applyResponse = await _client.PostAsJsonAsync("/v1/discover/apply", preview.Config, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        // registrado de verdade — /projects lista o projeto recém-aplicado sem restart do processo.
        var projects = await _client.GetFromJsonAsync<JsonElement>("/v1/projects", JsonOptions);
        Assert.Contains(projects.EnumerateArray(), p => p.GetProperty("id").GetString() == "found-project");
    }

    [Fact]
    public async Task GetProjectConfigReturnsAppliedToml()
    {
        var scanRoot = Directory.CreateTempSubdirectory().FullName;
        var newProjectDir = Path.Combine(scanRoot, "raw-project");
        Directory.CreateDirectory(newProjectDir);

        var preview = await (await _client.PostAsJsonAsync("/v1/discover/preview", new { path = newProjectDir, id = (string?)null }, JsonOptions))
            .Content.ReadFromJsonAsync<ConfigPreviewDto>(JsonOptions);
        await _client.PostAsJsonAsync("/v1/discover/apply", preview!.Config, JsonOptions);

        var config = await _client.GetFromJsonAsync<ProjectConfigDto>($"/v1/projects/{preview.Config.Id}/config", JsonOptions);
        Assert.NotNull(config);
        Assert.Equal("raw-project", config!.Id);
        Assert.Equal("raw", config.Type);
    }

    [Fact]
    public async Task GetProjectConfigForUnknownProjectIs404()
    {
        var response = await _client.GetAsync("/v1/projects/nope/config");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
