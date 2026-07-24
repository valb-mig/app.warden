using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Warden.Agent.Auth;
using Warden.Domain.Trust;
using Xunit;

namespace Warden.Agent.Tests;

public sealed class ScopedTokenTests : IAsyncLifetime
{
    private readonly WardenAgentFactory _factory = new();
    private HttpClient _client = null!;
    private ScopedTokenStore _scopedStore = null!;
    private string _masterToken = null!;

    public Task InitializeAsync()
    {
        var projectPath = Directory.CreateTempSubdirectory().FullName;
        _factory.WriteProject("proj-a", $"""
            id = "proj-a"
            type = "raw"
            path = "{projectPath}"
            [start]
            cmd = ["true"]
            """);
        _factory.WriteProject("proj-b", $"""
            id = "proj-b"
            type = "raw"
            path = "{projectPath}"
            [start]
            cmd = ["true"]
            """);

        _client = _factory.CreateClient();
        _masterToken = _factory.ApiToken;
        _scopedStore = _factory.Services.GetRequiredService<ScopedTokenStore>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SemToken_Retorna401()
    {
        var resp = await _client.GetAsync("/v1/projects/proj-a/status");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task MasterToken_AcessaQualquerProjeto()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _masterToken);

        var respA = await _client.GetAsync("/v1/projects/proj-a/status");
        var respB = await _client.GetAsync("/v1/projects/proj-b/status");

        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
    }

    [Fact]
    public async Task ScopedToken_AcessaProjetoPermitido()
    {
        var (_, rawToken) = _scopedStore.Create("ci-token", ["proj-a"]);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawToken);

        var resp = await _client.GetAsync("/v1/projects/proj-a/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ScopedToken_Bloqueado_ProjetoForaDoEscopo()
    {
        var (_, rawToken) = _scopedStore.Create("ci-token-a-only", ["proj-a"]);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawToken);

        var resp = await _client.GetAsync("/v1/projects/proj-b/status");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ScopedToken_Bloqueado_RotaGlobal()
    {
        var (_, rawToken) = _scopedStore.Create("ci-token-global", ["proj-a"]);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawToken);

        // Rotas globais (sem projectId na rota) não aceitam scoped tokens
        var resp = await _client.GetAsync("/v1/system/vitals");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ScopedToken_Revogado_Retorna401()
    {
        var (id, rawToken) = _scopedStore.Create("revogavel", ["proj-a"]);
        _scopedStore.Revoke(id);

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawToken);

        var resp = await _client.GetAsync("/v1/projects/proj-a/status");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ScopedTokenStore_List_RetornaTokensCriados()
    {
        var (id1, _) = _scopedStore.Create("token-1", ["proj-a"]);
        var (id2, _) = _scopedStore.Create("token-2", ["proj-a", "proj-b"]);

        var list = _scopedStore.List();

        Assert.Contains(list, t => t.Id == id1 && t.Label == "token-1" && !t.Revoked);
        Assert.Contains(list, t => t.Id == id2 && t.Label == "token-2" && t.AllowedProjectIds.Count == 2 && !t.Revoked);
    }

    [Fact]
    public async Task ScopedTokenStore_Revoke_MarcaComoRevogado()
    {
        var (id, _) = _scopedStore.Create("para-revogar", ["proj-a"]);
        _scopedStore.Revoke(id);

        var info = _scopedStore.List().Single(t => t.Id == id);
        Assert.True(info.Revoked);
    }
}
