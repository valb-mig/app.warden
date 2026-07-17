using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Warden.Agent.Api;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta dos endpoints de git (GET/POST `/projects/{id}/git...`) contra um
/// Kestrel/TestServer real — git real via subprocess, sem mock. Cobre a fatia "leitura+comandos" da
/// fase 8 do NEW_CONTEXT.md §12 (`Warden.Domain.Tests.GitServiceTests` já cobre a lógica de git em
/// si a fundo; aqui só valida o contrato HTTP por cima: auth, 404, shape do DTO, 400/409).
/// </summary>
public sealed class GitApiTests : IAsyncLifetime
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
    private string _projectPath = null!;

    public Task InitializeAsync()
    {
        _projectPath = Directory.CreateTempSubdirectory().FullName;
        RunGit(_projectPath, "init", "-b", "main");
        RunGit(_projectPath, "config", "user.email", "test@warden.local");
        RunGit(_projectPath, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(_projectPath, "a.txt"), "x");
        RunGit(_projectPath, "add", "a.txt");
        RunGit(_projectPath, "commit", "-m", "primeiro commit");

        _factory.WriteProject("p", ProjectTomlTemplate.Replace("{PROJECT_PATH}", _projectPath));
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

    private static void RunGit(string path, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(path);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        process.WaitForExit();
    }

    [Fact]
    public async Task GitInfoRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/projects/p/git");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GitInfoForUnknownProjectIs404()
    {
        var response = await _client.GetAsync("/projects/nope/git");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GitInfoReflectsRealRepoState()
    {
        var info = await _client.GetFromJsonAsync<GitInfoDto>("/projects/p/git", JsonOptions);

        Assert.NotNull(info);
        Assert.Equal("main", info!.Branch);
        Assert.False(info.Dirty);
        Assert.False(info.HasRemote);
        Assert.NotNull(info.LastCommit);
        Assert.Equal("primeiro commit", info.LastCommit!.Subject);
    }

    [Fact]
    public async Task GitCommandUnknownVerbIsBadRequest()
    {
        var response = await _client.PostAsync("/projects/p/git/clone", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GitPullWithoutConfirmIsConflict()
    {
        var response = await _client.PostAsync("/projects/p/git/pull", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GitFetchReturnsOkHttpResponseRegardlessOfCommandOutcome()
    {
        // Sem remote "origin" configurado o `git fetch` em si falha — mas isso é refletido no corpo
        // (`ok: false`), não num status HTTP de erro (mesmo contrato do endpoint Python).
        var response = await _client.PostAsync("/projects/p/git/fetch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GitCommandResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.False(result!.Ok);
    }
}
