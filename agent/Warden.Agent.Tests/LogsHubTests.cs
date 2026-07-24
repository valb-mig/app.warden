using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Warden.Domain;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Cliente SignalR real (não mockado) contra o `TestServer` — `TestServer` não faz WebSocket de
/// verdade, então o transporte cai pra long polling só nesse teste (produção usa o transporte
/// default do client, que negocia WebSocket normalmente contra um Kestrel real).
/// </summary>
public sealed class LogsHubTests : IAsyncLifetime
{
    private const string ProjectTomlTemplate = """
        id = "p"
        type = "raw"
        path = "{PROJECT_PATH}"

        [start]
        cmd = ["python3", "-c", "print('line-one'); print('line-two')"]
        capture_stdout = true
        """;

    private readonly WardenAgentFactory _factory = new();

    public Task InitializeAsync()
    {
        var projectPath = Directory.CreateTempSubdirectory().FullName;
        _factory.WriteProject("p", ProjectTomlTemplate.Replace("{PROJECT_PATH}", projectPath));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.ApiToken);
        await client.PostAsync("/v1/projects/p/stop", null);
        _factory.Dispose();
    }

    private HubConnection BuildConnection(string accessToken)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, $"/hubs/logs?access_token={accessToken}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    [Fact]
    public async Task ReceivesLogLinesAfterStart()
    {
        _factory.Services.GetRequiredService<Engine>().Approve("p");
        using var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", _factory.ApiToken);
        await http.PostAsync("/v1/projects/p/start", null);

        var received = new List<string>();
        var gotBothLines = new TaskCompletionSource();

        await using var connection = BuildConnection(_factory.ApiToken);
        connection.On<List<string>>("LogLines", lines =>
        {
            lock (received)
            {
                received.AddRange(lines);
                if (received.Any(l => l.Contains("line-one")) && received.Any(l => l.Contains("line-two")))
                {
                    gotBothLines.TrySetResult();
                }
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", "p", null);

        var finished = await Task.WhenAny(gotBothLines.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(gotBothLines.Task, finished);
    }

    [Fact]
    public async Task ConnectionIsClosedWithWrongToken()
    {
        var closed = new TaskCompletionSource();
        await using var connection = BuildConnection("wrong-token");
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync();
        }
        catch
        {
            // aceitável: o abort pode surgir como falha do próprio StartAsync dependendo do timing
            closed.TrySetResult();
        }

        var finished = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(closed.Task, finished);
    }
}
