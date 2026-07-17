using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Warden.Contracts.Admin;

namespace Warden.Admin.Ipc;

/// <summary>
/// Cliente HTTP sobre unix socket pra falar com a superfície de Admin do Agent (NEW_CONTEXT.md
/// §10.7) — nunca TCP/rede. `SocketsHttpHandler.ConnectCallback` troca o transporte por um
/// `Socket` unix de verdade; o resto (`HttpClient`) não sabe a diferença.
/// </summary>
public sealed class AdminApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public AdminApiClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://admin-socket") };
    }

    public async Task<IReadOnlyList<AdminProjectDto>> GetProjectsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<AdminProjectDto>>("/admin/projects", JsonOptions, ct) ?? [];

    public async Task<AdminProjectDto> ApproveAsync(string projectId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/admin/projects/{projectId}/approve", null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminProjectDto>(JsonOptions, ct))!;
    }

    public async Task<GlobalConfigDto> GetConfigAsync(CancellationToken ct = default) =>
        (await _http.GetFromJsonAsync<GlobalConfigDto>("/admin/config", JsonOptions, ct))!;

    public async Task<GlobalConfigDto> SaveConfigAsync(GlobalConfigDto config, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/admin/config", config, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GlobalConfigDto>(JsonOptions, ct))!;
    }

    /// <summary>Ping barato — usado pelo tray icon pra saber se o Agent está de pé, sem side effect.</summary>
    public async Task<bool> IsAgentUpAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/admin/projects", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
