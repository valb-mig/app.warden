using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Warden.Agent.Api;
using Warden.Contracts.Projects;
using Warden.Domain;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Integração de ponta a ponta contra um Kestrel/TestServer real (`WebApplicationFactory`) — sem
/// mock: registry/engine/adapters/sqlite reais, spawn real de `python3`/`echo`/`true`. Cobre a fase 5
/// (API REST) do NEW_CONTEXT.md §12.
/// </summary>
public sealed class ProjectApiTests : IAsyncLifetime
{
    private const string ProjectTomlTemplate = """
        id = "p"
        type = "raw"
        path = "{PROJECT_PATH}"

        [start]
        cmd = ["python3", "-c", "import time; print('hello'); time.sleep(5)"]
        capture_stdout = true

        [[actions]]
        name = "greet"
        cmd = ["echo", "hi"]

        [[actions]]
        name = "wipe"
        cmd = ["true"]
        destructive = true

        [[actions]]
        name = "shell"
        cmd = ["true"]
        interactive = true
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

    public async Task DisposeAsync()
    {
        await _client.PostAsync("/projects/p/stop", null);
        _factory.Dispose();
    }

    [Fact]
    public async Task ListProjectsRequiresAuth()
    {
        using var unauthenticated = _factory.CreateClient();

        var response = await unauthenticated.GetAsync("/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongTokenIsRejected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-the-token");

        var response = await client.GetAsync("/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListProjectsReturnsRegisteredProject()
    {
        var projects = await _client.GetFromJsonAsync<List<ProjectDto>>("/projects", JsonOptions);

        var project = Assert.Single(projects!);
        Assert.Equal("p", project.Id);
        Assert.Equal("raw", project.Type);
    }

    [Fact]
    public async Task StatusForUnknownProjectIs404()
    {
        var response = await _client.GetAsync("/projects/nope/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartRefusedUntilApproved()
    {
        var response = await _client.PostAsync("/projects/p/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StartStopLifecycleAfterApproval()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var start = await _client.PostAsync("/projects/p/start", null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        await WaitUntilAsync(async () =>
        {
            var status = await _client.GetFromJsonAsync<StatusDto>("/projects/p/status", JsonOptions);
            return status!.Running;
        });

        var stop = await _client.PostAsync("/projects/p/stop", null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        await WaitUntilAsync(async () =>
        {
            var status = await _client.GetFromJsonAsync<StatusDto>("/projects/p/status", JsonOptions);
            return !status!.Running;
        });
    }

    [Fact]
    public async Task ActionsListReflectsApprovalStatus()
    {
        var beforeApproval = await _client.GetFromJsonAsync<List<ActionDto>>("/projects/p/actions", JsonOptions);
        Assert.All(beforeApproval!, a => Assert.False(a.Approved));

        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var afterApproval = await _client.GetFromJsonAsync<List<ActionDto>>("/projects/p/actions", JsonOptions);
        Assert.All(afterApproval!, a => Assert.True(a.Approved));
        Assert.DoesNotContain(afterApproval!, a => a.Name == "start");
    }

    [Fact]
    public async Task RunNonDestructiveActionSucceeds()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var response = await _client.PostAsync("/projects/p/actions/greet", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ActionResultDto>(JsonOptions);
        Assert.Equal(0, result!.ExitCode);
        Assert.Contains("hi", result.Output);
    }

    [Fact]
    public async Task RunDestructiveActionWithoutConfirmIsConflict()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var response = await _client.PostAsync("/projects/p/actions/wipe", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RunDestructiveActionWithConfirmSucceeds()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var response = await _client.PostAsync("/projects/p/actions/wipe?confirm=true", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RunInteractiveActionIsBadRequest()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var response = await _client.PostAsync("/projects/p/actions/shell", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RunUnknownActionIs404()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");

        var response = await _client.PostAsync("/projects/p/actions/does-not-exist", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ServicesEndpointReturnsErrorPatterns()
    {
        var services = await _client.GetFromJsonAsync<ServicesDto>("/projects/p/services", JsonOptions);

        Assert.NotNull(services);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, double timeoutSeconds = 5.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(50);
        }
        Assert.Fail("condição não satisfeita dentro do timeout");
    }
}
