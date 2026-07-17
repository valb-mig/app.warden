using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Warden.Agent.Auth;

namespace Warden.Agent.Tests;

/// <summary>
/// `WebApplicationFactory` apontando pra um `~/.warden` descartável (config dir novo por teste) e
/// ambiente "Testing" — isso pula a resolução real de Tailscale/bind guard do `Program.cs` (só faz
/// sentido pra binder Kestrel de verdade, `TestServer` nunca escuta rede), sem mockar nenhuma lógica
/// de domínio: Engine/Registry/SqliteTrustStore/adapters continuam 100% reais.
/// </summary>
public sealed class WardenAgentFactory : WebApplicationFactory<Program>
{
    public string ConfigDir { get; } = Directory.CreateTempSubdirectory().FullName;

    public string ApiToken => Services.GetRequiredService<ApiTokenProvider>().Token;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConfigDir", ConfigDir);
    }

    public void WriteProject(string id, string toml) =>
        File.WriteAllText(Path.Combine(EnsureProjectsDir(), $"{id}.toml"), toml);

    private string EnsureProjectsDir() =>
        Directory.CreateDirectory(Path.Combine(ConfigDir, "projects")).FullName;
}
