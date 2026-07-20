using Microsoft.AspNetCore.Mvc;
using Warden.Contracts.Discovery;
using Warden.Domain;
using Warden.Domain.Config;
using Warden.Domain.Discovery;

namespace Warden.Agent.Api;

/// <summary>
/// Rotas de descoberta/sincronização de projeto — mirror do `discovery_routes.py` (TODO.md decisão
/// #17). Não é grupo `/admin`: mesma superfície pública que `/projects`/`/system`, protegida pelo
/// mesmo bearer token (ver Program.cs) — o Console/front já chama essas rotas hoje contra o engine
/// Python, então o contrato tem que bater 1:1 pro machine-switcher poder trocar de engine sem quebrar.
/// </summary>
public static class DiscoveryEndpoints
{
    public static RouteGroupBuilder MapDiscoveryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/scan-paths", (Engine engine) =>
            Results.Ok(new ScanPathsDto { ScanPaths = engine.LoadGlobalConfig().ScanPaths }));

        group.MapPost("/scan-paths", (ScanPathIn body, Engine engine) =>
            Results.Ok(new ScanPathsDto { ScanPaths = engine.AddScanPath(body.Path).ScanPaths }));

        group.MapDelete("/scan-paths", ([FromBody] ScanPathIn body, Engine engine) =>
            Results.Ok(new ScanPathsDto { ScanPaths = engine.RemoveScanPath(body.Path).ScanPaths }));

        group.MapGet("/browse", (string? path, Engine engine) =>
        {
            var result = engine.Browse(path);
            return Results.Ok(new BrowseResultDto
            {
                Path = result.Path,
                Parent = result.Parent,
                Entries = result.Entries.Select(e => new BrowseEntryDto { Name = e.Name, Path = e.Path }).ToList(),
            });
        });

        group.MapGet("/discover", (Engine engine) =>
            Results.Ok(new DiscoverResultDto { Projects = engine.Discover().Select(ToDto).ToList() }));

        group.MapPost("/discover/preview", (PreviewIn body, Engine engine) =>
        {
            var config = engine.PreviewConfig(body.Path, body.Id);
            return Results.Ok(new ConfigPreviewDto { Config = ToDto(config), Toml = Scaffold.RenderToml(config) });
        });

        group.MapPost("/discover/apply", (ProjectConfigDto body, Engine engine) =>
        {
            var toml = engine.ApplyConfig(FromDto(body));
            return Results.Ok(new ConfigPreviewDto { Config = body, Toml = toml });
        });

        return group;
    }

    public static DiscoveredProjectDto ToDto(DiscoveredProject p) => new() { Name = p.Name, Path = p.Path, Type = p.Type };

    public static ProjectConfigDto ToDto(ProjectConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Group = c.Group,
        Path = c.Path,
        Type = c.Type,
        ComposeFile = c.ComposeFile,
        ComposeServices = c.ComposeServices,
        Start = c.Start is null ? null : new StartConfigDto { Cmd = c.Start.Cmd, Cwd = c.Start.Cwd, CaptureStdout = c.Start.CaptureStdout },
        Notify = new NotifyConfigDto { OnError = c.Notify.OnError, OnFinished = c.Notify.OnFinished, OnGitBehind = c.Notify.OnGitBehind },
        Git = new GitWatchConfigDto { Watch = c.Git.Watch, Interval = c.Git.Interval, Remote = c.Git.Remote },
        LogSources = c.LogSources.Select(ls => new LogSourceDto
        {
            Name = ls.Name,
            Type = ls.Type,
            Path = ls.Path,
            Service = ls.Service,
            ErrorPatterns = ls.ErrorPatterns,
        }).ToList(),
        Actions = c.Actions.Select(a => new ActionConfigDto { Name = a.Name, Cmd = a.Cmd, Interactive = a.Interactive, Destructive = a.Destructive }).ToList(),
    };

    public static ProjectConfig FromDto(ProjectConfigDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Group = d.Group,
        Path = d.Path,
        Type = d.Type,
        ComposeFile = d.ComposeFile,
        ComposeServices = [.. d.ComposeServices],
        Start = d.Start is null ? null : new StartConfig { Cmd = [.. d.Start.Cmd], Cwd = d.Start.Cwd, CaptureStdout = d.Start.CaptureStdout },
        Notify = new NotifyConfig { OnError = d.Notify.OnError, OnFinished = d.Notify.OnFinished, OnGitBehind = d.Notify.OnGitBehind },
        Git = new GitWatchConfig { Watch = d.Git.Watch, Interval = d.Git.Interval, Remote = d.Git.Remote },
        LogSources = [.. d.LogSources.Select(ls => new LogSource { Name = ls.Name, Type = ls.Type, Path = ls.Path, Service = ls.Service, ErrorPatterns = [.. ls.ErrorPatterns] })],
        Actions = [.. d.Actions.Select(a => new ActionConfig { Name = a.Name, Cmd = [.. a.Cmd], Interactive = a.Interactive, Destructive = a.Destructive })],
    };

    public sealed record ScanPathIn(string Path);

    public sealed record PreviewIn(string Path, string? Id);
}
