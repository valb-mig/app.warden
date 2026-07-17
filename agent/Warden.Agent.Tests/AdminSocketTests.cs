using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Warden.Contracts.Admin;
using Xunit;

namespace Warden.Agent.Tests;

/// <summary>
/// Sobe o binário real do Agent (não `WebApplicationFactory`/`TestServer` — esses não abrem socket
/// de verdade, então não servem pra validar a superfície de Admin) com `ConfigDir`/`XDG_RUNTIME_DIR`
/// isolados por teste. Exige Tailscale real ativo nesta máquina (mesma exigência de
/// `TailscaleTransportTests`) — pula graciosamente se não tiver.
/// </summary>
public sealed class AdminSocketTests : IAsyncLifetime
{
    private const int TestPort = 18420;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private string _runtimeDir = null!;
    private Process? _process;
    private string? _consoleBaseUrl;

    public async Task InitializeAsync()
    {
        var configDir = Directory.CreateTempSubdirectory().FullName;
        _runtimeDir = Directory.CreateTempSubdirectory().FullName;

        var psi = new ProcessStartInfo
        {
            FileName = FindAgentExecutable(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add($"--ConfigDir={configDir}");
        psi.ArgumentList.Add($"--ApiPort={TestPort}");
        psi.Environment["XDG_RUNTIME_DIR"] = _runtimeDir;

        _process = Process.Start(psi)!;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        const string marker = "Now listening on: ";
        while (DateTime.UtcNow < deadline)
        {
            var line = await _process.StandardOutput.ReadLineAsync();
            if (line is null) break;

            var idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                _consoleBaseUrl = line[(idx + marker.Length)..].Trim();
                break;
            }
        }

        if (_consoleBaseUrl is not null)
        {
            await WaitForSocketAsync();
        }
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }
        return Task.CompletedTask;
    }

    private static string FindAgentExecutable()
    {
        var netDir = new DirectoryInfo(AppContext.BaseDirectory);
        var configName = netDir.Parent!.Name;
        var agentRoot = netDir.Parent!.Parent!.Parent!.Parent!.FullName;
        var exe = Path.Combine(agentRoot, "Warden.Agent", "bin", configName, netDir.Name, "Warden.Agent");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"binário do Agent não encontrado em {exe} — rode `dotnet build` primeiro");
        }
        return exe;
    }

    private HttpClient CreateSocketClient()
    {
        var socketPath = Path.Combine(_runtimeDir, "warden", "admin.sock");
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        return new HttpClient(handler) { BaseAddress = new Uri("http://admin-socket") };
    }

    private async Task WaitForSocketAsync()
    {
        var socketPath = Path.Combine(_runtimeDir, "warden", "admin.sock");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(socketPath)) return;
            await Task.Delay(50);
        }
    }

    [SkippableFact]
    public async Task AdminProjectsReachableOverUnixSocket()
    {
        Skip.If(_consoleBaseUrl is null, "Agent não subiu (sem tailscale ativo nesta máquina?)");
        using var client = CreateSocketClient();

        var response = await client.GetAsync("/admin/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task AdminRoutesRejectedOverConsoleNetworkListener()
    {
        Skip.If(_consoleBaseUrl is null, "Agent não subiu (sem tailscale ativo nesta máquina?)");
        using var client = new HttpClient { BaseAddress = new Uri(_consoleBaseUrl!) };

        var response = await client.GetAsync("/admin/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task ConfigRoundTripsOverUnixSocket()
    {
        Skip.If(_consoleBaseUrl is null, "Agent não subiu (sem tailscale ativo nesta máquina?)");
        using var client = CreateSocketClient();

        var config = await client.GetFromJsonAsync<GlobalConfigDto>("/admin/config", JsonOptions);
        Assert.NotNull(config);

        var updated = config! with { NotifyChannel = "ntfy", NtfyTopic = "warden-test" };
        var postResponse = await client.PostAsJsonAsync("/admin/config", updated, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var reloaded = await client.GetFromJsonAsync<GlobalConfigDto>("/admin/config", JsonOptions);
        Assert.Equal("ntfy", reloaded!.NotifyChannel);
        Assert.Equal("warden-test", reloaded.NtfyTopic);
    }
}
