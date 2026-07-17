namespace Warden.Agent.Auth;

/// <summary>Guarda o token bearer já carregado, pra injeção em endpoints/Hub sem reler o arquivo.</summary>
public sealed class ApiTokenProvider(string token)
{
    public string Token { get; } = token;
}
