using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Warden.Contracts.Admin;
using Warden.Contracts.Discovery;
using Warden.Contracts.Projects;

namespace Warden.Admin.Ipc;

/// <summary>Erro HTTP vindo do Agent — espelha a classe <c>ApiError</c> do front Next.js (web/src/lib/api.ts).</summary>
public sealed class AgentApiException(int status, string detail) : Exception(detail)
{
    public int Status { get; } = status;
}

/// <summary>
/// Cliente HTTP sobre unix socket pra falar com o Agent — tanto a superfície exclusiva de Admin
/// (NEW_CONTEXT.md §10.7: aprovar projeto, config global, sem bearer token — gate é o socket) quanto
/// as mesmas rotas `/projects`/`/system`/descoberta que o Console/Next.js usa pela rede (essas exigem
/// o mesmo bearer token que o Console usa, ver `RequireToken` em Program.cs). Isso funciona porque só
/// o grupo `/admin` é filtrado pra unix-socket-only no Program.cs — as demais respondem em qualquer
/// listener, incluindo esse socket local. Resultado: o Admin ganha paridade de feature com o front
/// sem duplicar nenhuma rota nova no Agent. `SocketsHttpHandler.ConnectCallback` troca o transporte
/// por um `Socket` unix de verdade; o resto (`HttpClient`) não sabe a diferença.
/// </summary>
public sealed class AgentApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly string _configDir;
    private string? _apiToken;

    public AgentApiClient(string socketPath, string configDir)
    {
        _configDir = configDir;
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

    // ---- Admin-only (grupo /admin, unix-socket-only e sem bearer token no Program.cs) ----

    public async Task<IReadOnlyList<AdminProjectDto>> GetAdminProjectsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<AdminProjectDto>>("/admin/projects", JsonOptions, ct) ?? [];

    public async Task<AdminProjectDto> ApproveAsync(string projectId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/admin/projects/{projectId}/approve", null, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<AdminProjectDto>(JsonOptions, ct))!;
    }

    public async Task<GlobalConfigDto> GetConfigAsync(CancellationToken ct = default) =>
        (await _http.GetFromJsonAsync<GlobalConfigDto>("/admin/config", JsonOptions, ct))!;

    public async Task<GlobalConfigDto> SaveConfigAsync(GlobalConfigDto config, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/admin/config", config, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
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

    // ---- Rotas públicas de projeto/sistema (mesmo contrato do web/src/lib/api.ts, exigem bearer token) ----

    public Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDto>>("/projects", ct);

    public async Task StartAsync(string projectId, CancellationToken ct = default) =>
        await SendPostAsync($"/projects/{projectId}/start", ct);

    public async Task StopAsync(string projectId, CancellationToken ct = default) =>
        await SendPostAsync($"/projects/{projectId}/stop", ct);

    public Task<StatusDto> GetStatusAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<StatusDto>($"/projects/{projectId}/status", ct);

    public Task<LogsDto> GetLogsAsync(string projectId, int tail = 300, string? service = null, CancellationToken ct = default)
    {
        var qs = service is null ? $"?tail={tail}" : $"?tail={tail}&service={Uri.EscapeDataString(service)}";
        return GetAsync<LogsDto>($"/projects/{projectId}/logs{qs}", ct);
    }

    public Task<ServicesDto> GetServicesAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<ServicesDto>($"/projects/{projectId}/services", ct);

    public Task<IReadOnlyList<ActionDto>> ListActionsAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ActionDto>>($"/projects/{projectId}/actions", ct);

    public async Task<ActionResultDto> RunActionAsync(string projectId, string actionName, bool confirm = false, CancellationToken ct = default)
    {
        var response = await SendPostAsync($"/projects/{projectId}/actions/{actionName}?confirm={(confirm ? "true" : "false")}", ct);
        return (await response.Content.ReadFromJsonAsync<ActionResultDto>(JsonOptions, ct))!;
    }

    public Task<LanguagesDto> GetLanguagesAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<LanguagesDto>($"/projects/{projectId}/languages", ct);

    public Task<GitInfoDto?> GetGitInfoAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<GitInfoDto?>($"/projects/{projectId}/git", ct);

    public async Task<GitCommandResultDto> GitCommandAsync(string projectId, string verb, bool confirm = false, CancellationToken ct = default)
    {
        var response = await SendPostAsync($"/projects/{projectId}/git/{verb}?confirm={(confirm ? "true" : "false")}", ct);
        return (await response.Content.ReadFromJsonAsync<GitCommandResultDto>(JsonOptions, ct))!;
    }

    public Task<SystemVitalsDto> GetSystemVitalsAsync(CancellationToken ct = default) =>
        GetAsync<SystemVitalsDto>("/system/vitals", ct);

    public Task<IReadOnlyList<HistoryEventDto>> GetHistoryAsync(string projectId, int limit = 20, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<HistoryEventDto>>($"/projects/{projectId}/history?limit={limit}", ct);

    // ---- Descoberta/sincronização de projeto (mesmo contrato, exige bearer token) ----

    public Task<ScanPathsDto> GetScanPathsAsync(CancellationToken ct = default) =>
        GetAsync<ScanPathsDto>("/scan-paths", ct);

    public async Task<ScanPathsDto> AddScanPathAsync(string path, CancellationToken ct = default)
    {
        var response = await SendPostJsonAsync("/scan-paths", new { path }, ct);
        return (await response.Content.ReadFromJsonAsync<ScanPathsDto>(JsonOptions, ct))!;
    }

    public async Task<ScanPathsDto> RemoveScanPathAsync(string path, CancellationToken ct = default)
    {
        var response = await SendDeleteJsonAsync("/scan-paths", new { path }, ct);
        return (await response.Content.ReadFromJsonAsync<ScanPathsDto>(JsonOptions, ct))!;
    }

    public Task<BrowseResultDto> BrowseAsync(string? path, CancellationToken ct = default) =>
        GetAsync<BrowseResultDto>(path is null ? "/browse" : $"/browse?path={Uri.EscapeDataString(path)}", ct);

    public Task<DiscoverResultDto> DiscoverAsync(CancellationToken ct = default) =>
        GetAsync<DiscoverResultDto>("/discover", ct);

    public async Task<ConfigPreviewDto> PreviewConfigAsync(string path, string? id = null, CancellationToken ct = default)
    {
        var response = await SendPostJsonAsync("/discover/preview", new { path, id }, ct);
        return (await response.Content.ReadFromJsonAsync<ConfigPreviewDto>(JsonOptions, ct))!;
    }

    public async Task<ConfigPreviewDto> ApplyConfigAsync(ProjectConfigDto config, CancellationToken ct = default)
    {
        var response = await SendPostJsonAsync("/discover/apply", config, ct);
        return (await response.Content.ReadFromJsonAsync<ConfigPreviewDto>(JsonOptions, ct))!;
    }

    public Task<ProjectConfigDto> GetProjectConfigAsync(string projectId, CancellationToken ct = default) =>
        GetAsync<ProjectConfigDto>($"/projects/{projectId}/config", ct);

    // ---- infra ----

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, path, null, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    private Task<HttpResponseMessage> SendPostAsync(string path, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, null, ct);

    private Task<HttpResponseMessage> SendPostJsonAsync(string path, object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, body, ct);

    private Task<HttpResponseMessage> SendDeleteJsonAsync(string path, object body, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, path, body, ct);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? jsonBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody, inputType: jsonBody.GetType(), options: JsonOptions);
        }
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ResolveApiToken()}");
        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return response;
    }

    /// <summary>
    /// Lê `~/.warden/api_token` diretamente (mesmo arquivo que `TokenStore.LoadOrCreate` usa no Agent)
    /// em vez de referenciar Warden.Domain só por isso — Admin fica ignorante do resto do domínio,
    /// só precisa saber onde o Agent guarda o token. Cacheado após a primeira leitura bem-sucedida.
    /// </summary>
    private string ResolveApiToken()
    {
        if (_apiToken is not null) return _apiToken;
        var path = Path.Combine(_configDir, "api_token");
        if (!File.Exists(path))
        {
            throw new AgentApiException(0, "token da API não encontrado — o Agent já rodou nesta máquina?");
        }
        _apiToken = File.ReadAllText(path).Trim();
        return _apiToken;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = response.ReasonPhrase ?? "erro desconhecido";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (body.TryGetProperty("detail", out var detailProp) && detailProp.GetString() is { } d)
            {
                detail = d;
            }
        }
        catch (JsonException)
        {
            // corpo não é JSON (ex: 404 puro sem handler) — mantém o reason phrase.
        }
        throw new AgentApiException((int)response.StatusCode, detail);
    }

    public void Dispose() => _http.Dispose();
}
