using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Warden.Ator.Transport;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var apiPort = builder.Configuration.GetValue("ApiPort", 8420);

// Resolve o IP antes de configurar o Kestrel — a defesa primária do assert de bind (NEW_CONTEXT.md
// §10.2) é nunca dar ao Kestrel a chance de escolher um endereço (nada de `UseUrls`/wildcard aqui).
IPlateiaTransport transport = new TailscaleTransport();
System.Net.IPEndPoint plateiaEndpoint;
try
{
    plateiaEndpoint = transport.ResolveEndpoint(apiPort);
}
catch (TailscaleUnavailableException ex)
{
    Console.Error.WriteLine($"[Warden.Ator] boot abortado — {ex.Message}");
    Console.Error.WriteLine(
        "[Warden.Ator] a superfície da Plateia só sobe na interface do Tailscale; sem IP resolvido, " +
        "o Ator recusa bindar em qualquer outra interface (ver NEW_CONTEXT.md §10.2).");
    return 1;
}

builder.WebHost.ConfigureKestrel(options => options.Listen(plateiaEndpoint));

var app = builder.Build();

// Defesa em profundidade: confirma que o bind real bate com o esperado assim que o Kestrel sobe.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var boundAddresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses.ToList() ?? [];
    try
    {
        BindGuard.AssertSingleExpectedAddress(boundAddresses, plateiaEndpoint);
    }
    catch (BindAssertionException ex)
    {
        Environment.FailFast(
            $"[Warden.Ator] assert de bind da Plateia falhou: {ex.Message}. " +
            "Isso indica 0.0.0.0/UseUrls acidental ou config divergente — não é seguro continuar.");
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { service = "warden-ator", status = "ok" }));

app.Run();
return 0;
