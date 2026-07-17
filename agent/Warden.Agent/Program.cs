using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Warden.Agent.Api;
using Warden.Agent.Auth;
using Warden.Agent.Hubs;
using Warden.Agent.Transport;
using Warden.Domain;
using Warden.Domain.Config;
using Warden.Domain.Trust;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var apiPort = builder.Configuration.GetValue("ApiPort", 8420);
var configDir = builder.Configuration.GetValue<string>("ConfigDir")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".warden");
Directory.CreateDirectory(configDir);

var registry = new Registry(configDir);
var trustStore = new SqliteTrustStore(Path.Combine(configDir, "warden.db"));
var manifestRegistry = new ManifestRegistry(trustStore);
var engine = new Engine(registry, manifestRegistry);
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

var app = builder.Build();

if (!isTesting && consoleEndpoint is not null)
{
    var boundEndpoint = consoleEndpoint;

    // Defesa em profundidade: confirma que o bind real bate com o esperado assim que o Kestrel sobe.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var boundAddresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? [];
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
        ConfirmationRequiredException => (StatusCodes.Status409Conflict, error.Message),
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

app.MapGroup("/projects")
    .AddEndpointFilter(async (context, next) =>
    {
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        return header == $"Bearer {apiToken}" ? await next(context) : Results.Unauthorized();
    })
    .MapProjectEndpoints();

app.MapHub<LogsHub>("/hubs/logs");

app.Run();
return 0;

public partial class Program;
