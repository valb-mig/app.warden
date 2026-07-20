using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Warden.Agent.Admin;
using Warden.Agent.Api;
using Warden.Agent.Auth;
using Warden.Agent.Hubs;
using Warden.Agent.Transport;
using Warden.Domain;
using Warden.Domain.Config;
using Warden.Domain.Events;
using Warden.Domain.Git;
using Warden.Domain.Notify;
using Warden.Domain.Trust;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var configDir = builder.Configuration.GetValue<string>("ConfigDir")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".warden");
Directory.CreateDirectory(configDir);

var globalConfigPath = Path.Combine(configDir, "config.toml");
var globalConfig = ConfigLoader.LoadGlobalConfig(globalConfigPath);
var apiPort = builder.Configuration.GetValue<int?>("ApiPort") ?? globalConfig.ApiPort;

var registry = new Registry(configDir);
var dbPath = Path.Combine(configDir, "warden.db");
var trustStore = new SqliteTrustStore(dbPath);
var manifestRegistry = new ManifestRegistry(trustStore);
var eventStore = new SqliteEventStore(dbPath);
var notifier = NotifierFactory.Create(globalConfig);
var engine = new Engine(registry, manifestRegistry, eventStore, notifier);
engine.Boot();

var apiToken = TokenStore.LoadOrCreate(Path.Combine(configDir, "api_token"));

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton<ChildProcessRegistry>();
builder.Services.AddSingleton(new ApiTokenProvider(apiToken));

// Testes usam WebApplicationFactory com Environment="Testing" — não faz sentido exigir Tailscale
// real nem bindar Kestrel num IP real durante teste (TestServer não escuta rede de verdade mesmo).
var isTesting = builder.Environment.IsEnvironment("Testing");

IPEndPoint? consoleEndpoint = null;
if (!isTesting)
{
    // Resolve o IP antes de configurar o Kestrel — a defesa primária do assert de bind (NEW_CONTEXT.md
    // §10.2) é nunca dar ao Kestrel a chance de escolher um endereço (nada de `UseUrls`/wildcard aqui).
    IConsoleTransport transport = new TailscaleTransport();
    try
    {
        consoleEndpoint = transport.ResolveEndpoint(apiPort);
    }
    catch (TailscaleUnavailableException ex)
    {
        Console.Error.WriteLine($"[Warden.Agent] boot abortado — {ex.Message}");
        Console.Error.WriteLine(
            "[Warden.Agent] a superfície do Console só sobe na interface do Tailscale; sem IP resolvido, " +
            "o Agent recusa bindar em qualquer outra interface (ver NEW_CONTEXT.md §10.2).");
        return 1;
    }

    builder.WebHost.ConfigureKestrel(options => options.Listen(consoleEndpoint));
}

// Superfície de Admin: unix socket local, nunca pela rede (NEW_CONTEXT.md §10.7). Sem dependência de
// Tailscale — fica de fora só em teste (WebApplicationFactory usa TestServer, que nunca bind real
// mesmo) e no Windows (named pipe ainda não implementado, ver Admin/AdminSocket.cs).
var adminSocketPath = !isTesting && !OperatingSystem.IsWindows() ? AdminSocket.ResolvePath(configDir) : null;
if (adminSocketPath is not null)
{
    builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(adminSocketPath));
}

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(engine.Shutdown);

if (adminSocketPath is not null)
{
    app.Lifetime.ApplicationStarted.Register(() => AdminSocket.Secure(adminSocketPath));
}

if (!isTesting && consoleEndpoint is not null)
{
    var boundEndpoint = consoleEndpoint;

    // Defesa em profundidade: confirma que o bind real bate com o esperado assim que o Kestrel sobe.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        // Filtra o endereço do unix socket do Admin antes de checar — o bind guard cuida só da
        // superfície do Console/Tailscale, o socket já tem sua própria garantia (AdminSocket + filtro
        // de LocalIpAddress); as duas coisas coexistem de propósito, não é bind divergente.
        var boundAddresses = (app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Where(a => !a.StartsWith("http://unix:", StringComparison.Ordinal))
            .ToList();
        try
        {
            BindGuard.AssertSingleExpectedAddress(boundAddresses, boundEndpoint);
        }
        catch (BindAssertionException ex)
        {
            Environment.FailFast(
                $"[Warden.Agent] assert de bind do Console falhou: {ex.Message}. " +
                "Isso indica 0.0.0.0/UseUrls acidental ou config divergente — não é seguro continuar.");
        }
    });
}

// Auth é bearer token manual (não cookie), então CORS aberto não abre brecha real: um site de
// terceiro não tem como adivinhar o token pra montar o header Authorization (mesma nota do Python).
app.UseCors();

// Mapeia exceções de domínio pra status HTTP — equivalente centralizado aos `@app.exception_handler`
// do FastAPI: as rotas só deixam a exceção propagar, sem try/catch espalhado.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, detail) = error switch
    {
        KeyNotFoundException => (StatusCodes.Status404NotFound, error.Message),
        ManifestNotApprovedException => (StatusCodes.Status403Forbidden, error.Message),
        ActionInteractiveException => (StatusCodes.Status400BadRequest, error.Message),
        GitVerbNotSupportedException => (StatusCodes.Status400BadRequest, error.Message),
        ConfirmationRequiredException => (StatusCodes.Status409Conflict, error.Message),
        DirectoryNotFoundException => (StatusCodes.Status400BadRequest, error.Message),
        _ => (StatusCodes.Status500InternalServerError, "erro interno"),
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { detail });
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { service = "warden-agent", status = "ok" }));

async ValueTask<object?> RequireToken(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
{
    var header = context.HttpContext.Request.Headers.Authorization.ToString();
    return header == $"Bearer {apiToken}" ? await next(context) : Results.Unauthorized();
}

app.MapGroup("/projects").AddEndpointFilter(RequireToken).MapProjectEndpoints();
app.MapGroup("/system").AddEndpointFilter(RequireToken).MapSystemEndpoints();

// Descoberta/sincronização (scan-paths/discover/browse) — mesma superfície pública que /projects e
// /system (não é /admin), protegida pelo mesmo token: o Console/front já chama essas rotas contra o
// engine Python, o contrato precisa bater 1:1 pro machine-switcher poder trocar de engine.
app.MapGroup("").AddEndpointFilter(RequireToken).MapDiscoveryEndpoints();

app.MapHub<LogsHub>("/hubs/logs");

if (adminSocketPath is not null)
{
    app.MapGroup("/admin")
        .AddEndpointFilter(async (context, next) =>
        {
            // Garantia estrutural, não só "não mapeamos essas rotas lá fora": uma conexão que chegou
            // pelo listener TCP (Console/Tailscale) sempre tem LocalIpAddress preenchido; só o unix
            // socket do Admin deixa esse campo null. Se algo um dia proxiar `/admin` pra fora, isso
            // recusa mesmo assim.
            return context.HttpContext.Connection.LocalIpAddress is null
                ? await next(context)
                : Results.NotFound();
        })
        .MapAdminEndpoints(globalConfigPath);
}

app.Run();
return 0;

public partial class Program;
