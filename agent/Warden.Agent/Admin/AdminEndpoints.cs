using System.Text.Json;
using Warden.Contracts.Admin;
using Warden.Domain;
using Warden.Domain.Config;
using Warden.Domain.Trust;

namespace Warden.Agent.Admin;

/// <summary>
/// Rotas exclusivas do Admin — nunca expostas pela Plateia/Console (ver NEW_CONTEXT.md §10.3/§10.7).
/// A garantia estrutural fica no `AddEndpointFilter` do grupo em Program.cs (rejeita qualquer request
/// cuja conexão tenha `LocalIpAddress` != null, isto é, que chegou por um listener de rede em vez do
/// unix socket) — não é só "não mapeamos essas rotas lá fora", é ativamente recusado se acontecer.
/// </summary>
public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group, string configPath)
    {
        group.MapGet("/projects", (Engine engine) => engine.AllProjects().Select(p => ToDto(engine, p.Id)));

        group.MapPost("/projects/{projectId}/approve", (string projectId, Engine engine) =>
        {
            engine.Approve(projectId);
            return Results.Ok(ToDto(engine, projectId));
        });

        group.MapGet("/config", () => Results.Ok(ToDto(ConfigLoader.LoadGlobalConfig(configPath))));

        group.MapPost("/config", (GlobalConfigDto dto) =>
        {
            var config = new GlobalConfig
            {
                ApiPort = dto.ApiPort,
                NotifyChannel = dto.NotifyChannel,
                NtfyTopic = dto.NtfyTopic,
                NtfyServer = dto.NtfyServer,
                ScanPaths = [.. dto.ScanPaths],
            };
            ConfigLoader.SaveGlobalConfig(configPath, config);
            return Results.Ok(ToDto(config));
        });

        // Env vars — só pelo socket Admin; valores nunca saem pela rede Console (masked no GET)
        group.MapGet("/projects/{projectId}/env", (string projectId, Engine engine) =>
        {
            var config = engine.GetProjectConfigFile(projectId);
            var masked = config.Env.ToDictionary(kv => kv.Key, _ => "***");
            return Results.Ok(masked);
        });

        group.MapPost("/projects/{projectId}/env", (string projectId, Dictionary<string, string> env, Engine engine) =>
        {
            var c = engine.GetProjectConfigFile(projectId);
            var updated = new ProjectConfig
            {
                Id = c.Id,
                Name = c.Name,
                Group = c.Group,
                Path = c.Path,
                Type = c.Type,
                ComposeFile = c.ComposeFile,
                ComposeServices = c.ComposeServices,
                Start = c.Start,
                Notify = c.Notify,
                Git = c.Git,
                LogSources = c.LogSources,
                Actions = c.Actions,
                Env = env,
            };
            engine.ApplyConfig(updated);
            return Results.Ok(env.ToDictionary(kv => kv.Key, _ => "***"));
        });

        return group;
    }

    private static AdminProjectDto ToDto(Engine engine, string projectId)
    {
        var project = engine.GetProject(projectId);
        var snapshot = engine.Manifest(projectId);
        return new AdminProjectDto
        {
            Id = project.Id,
            Name = project.DisplayName,
            Type = project.Type,
            Status = snapshot.Status.ToString(),
            Commands = snapshot.Manifest.Commands.Select(c => c.Name).ToList(),
            ApprovedCommands = snapshot.ApprovedManifestJson is { } json
                ? JsonSerializer.Deserialize<List<ManifestCommand>>(json)!.Select(c => c.Name).ToList()
                : null,
        };
    }

    private static GlobalConfigDto ToDto(GlobalConfig config) => new()
    {
        ApiPort = config.ApiPort,
        NotifyChannel = config.NotifyChannel,
        NtfyTopic = config.NtfyTopic,
        NtfyServer = config.NtfyServer,
        ScanPaths = config.ScanPaths,
    };
}
