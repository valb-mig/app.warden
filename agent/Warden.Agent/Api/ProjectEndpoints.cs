using Warden.Domain;
using Warden.Domain.Trust;

namespace Warden.Agent.Api;

/// <summary>
/// Rotas REST de projeto — mesmo contrato do FastAPI atual sempre que possível (ver NEW_CONTEXT.md
/// §12 fase 5). Git/linguagens/histórico/audit ficam de fora por enquanto: dependem de domínio ainda
/// não portado (`git.py`, `languages.py`, `EventStore`) — entram na fase 8 (paridade de feature).
/// Erros viram resposta HTTP via `app.UseExceptionHandler` no Program.cs, não try/catch por rota
/// (mesmo padrão centralizado do `@app.exception_handler` do Python).
/// </summary>
public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (Engine engine) => engine.AllProjects().Select(p => new ProjectDto
        {
            Id = p.Id,
            Name = p.DisplayName,
            Type = p.Type,
            Group = p.Group,
        }));

        group.MapPost("/{projectId}/start", (string projectId, Engine engine) =>
        {
            engine.Start(projectId);
            return Results.Ok(new { ok = true });
        });

        group.MapPost("/{projectId}/stop", (string projectId, Engine engine) =>
        {
            engine.GetProject(projectId);
            engine.Stop(projectId);
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/{projectId}/status", (string projectId, Engine engine) =>
        {
            engine.GetProject(projectId);
            var s = engine.Status(projectId);
            return Results.Ok(new StatusDto
            {
                Running = s.Running,
                Pid = s.Pid,
                Ports = s.Ports,
                UptimeSeconds = s.UptimeSeconds,
                CpuPercent = s.CpuPercent,
                MemoryMb = s.MemoryMb,
            });
        });

        group.MapGet("/{projectId}/logs", (string projectId, Engine engine, int tail = 100, string? service = null) =>
        {
            engine.GetProject(projectId);
            return Results.Ok(new LogsDto { Lines = engine.Logs(projectId, tail, service) });
        });

        group.MapGet("/{projectId}/services", (string projectId, Engine engine) =>
        {
            var project = engine.GetProject(projectId);
            var patterns = project.LogSources.SelectMany(s => s.ErrorPatterns).Distinct().ToList();
            return Results.Ok(new ServicesDto { Services = engine.Services(projectId), ErrorPatterns = patterns });
        });

        group.MapGet("/{projectId}/actions", (string projectId, Engine engine) =>
        {
            engine.GetProject(projectId);
            var snapshot = engine.Manifest(projectId);
            var approved = snapshot.Status == TrustStatus.Approved;
            return Results.Ok(snapshot.Manifest.Commands
                .Where(c => c.Name != "start")
                .Select(c => new ActionDto
                {
                    Name = c.Name,
                    Interactive = c.Interactive,
                    Destructive = c.Destructive,
                    Approved = approved,
                }));
        });

        group.MapPost("/{projectId}/actions/{actionName}", (string projectId, string actionName, Engine engine, bool confirm = false) =>
        {
            engine.GetProject(projectId);
            var result = engine.RunAction(projectId, actionName, confirm);
            return Results.Ok(new ActionResultDto { ExitCode = result.ExitCode, Output = result.Output });
        });

        group.MapGet("/{projectId}/languages", (string projectId, Engine engine) =>
        {
            engine.GetProject(projectId);
            return Results.Ok(new LanguagesDto { Languages = engine.Languages(projectId) });
        });

        group.MapGet("/{projectId}/git", (string projectId, Engine engine) =>
        {
            engine.GetProject(projectId);
            var info = engine.GitInfo(projectId);
            if (info is null) return Results.Ok((GitInfoDto?)null);
            return Results.Ok(new GitInfoDto
            {
                Branch = info.Branch,
                Dirty = info.Dirty,
                DirtyCount = info.DirtyCount,
                Ahead = info.Ahead,
                Behind = info.Behind,
                HasRemote = info.HasRemote,
                LastCommit = info.LastCommit is { } commit
                    ? new GitCommitDto
                    {
                        Hash = commit.Hash,
                        Subject = commit.Subject,
                        Author = commit.Author,
                        Relative = commit.Relative,
                    }
                    : null,
            });
        });

        group.MapPost("/{projectId}/git/{verb}", (string projectId, string verb, Engine engine, bool confirm = false) =>
        {
            engine.GetProject(projectId);
            var result = engine.GitCommand(projectId, verb, confirm);
            return Results.Ok(new GitCommandResultDto
            {
                Ok = result.Ok,
                ExitCode = result.ExitCode,
                Output = result.Output,
                Refused = result.Refused,
            });
        });

        return group;
    }
}
