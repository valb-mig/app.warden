using System.Collections.Concurrent;
using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Warden.Domain.Trust;

namespace Warden.Domain;

/// <summary>
/// Facade que amarra <see cref="Registry"/> + <see cref="AdapterFactory"/> + <see cref="ManifestRegistry"/>
/// pro Agent consumir — equivalente ao `Engine` do engine Python, mas escopado só ao que já foi
/// portado (bus/notifier/watchers/history/git/languages ficam pra fase 8, paridade de feature).
/// </summary>
public sealed class Engine(Registry registry, ManifestRegistry manifestRegistry)
{
    private readonly ConcurrentDictionary<string, IAdapter> _adapters = new();

    public void Boot()
    {
        registry.Load();
        foreach (var project in registry.All())
        {
            manifestRegistry.Refresh(project);
        }
    }

    public IReadOnlyList<ProjectConfig> AllProjects() => registry.All();

    public ProjectConfig GetProject(string projectId) => registry.Get(projectId);

    /// <summary>
    /// Start passa pelo mesmo trust gate das ações (§10.3/10.4) — o comando `[start]` também faz
    /// parte do `CommandManifest`, então mudá-lo também exige reaprovação antes de rodar de novo.
    /// </summary>
    public void Start(string projectId)
    {
        if (Manifest(projectId).Status != TrustStatus.Approved)
        {
            throw new ManifestNotApprovedException(
                $"projeto \"{projectId}\" não está aprovado — aprove no Admin antes de iniciar");
        }
        GetAdapter(projectId).Start();
    }

    public void Stop(string projectId) => GetAdapter(projectId).Stop();

    public ProcessStatus Status(string projectId) => GetAdapter(projectId).Status();

    public IReadOnlyList<string> Logs(string projectId, int tail = 100, string? service = null) =>
        GetAdapter(projectId).Logs(tail, service);

    public IReadOnlyList<string> Services(string projectId) => GetAdapter(projectId).Services();

    public ProjectManifestSnapshot Manifest(string projectId) =>
        manifestRegistry.Get(projectId)
        ?? throw new KeyNotFoundException($"manifesto do projeto \"{projectId}\" ainda não foi resolvido");

    /// <summary>
    /// Passthrough pro <see cref="ManifestRegistry.Approve"/> — chamada só por caminho local/confiável
    /// (hoje: testes; a partir da fase 6, o handler IPC do Admin). Não existe rota HTTP pra isso na
    /// Plateia/Console, de propósito (ver NEW_CONTEXT.md §10.3).
    /// </summary>
    public ProjectManifestSnapshot Approve(string projectId) => manifestRegistry.Approve(GetProject(projectId));

    public ActionExecutionResult RunAction(string projectId, string actionName, bool confirmed)
    {
        var snapshot = Manifest(projectId);
        var command = snapshot.Manifest.Commands.FirstOrDefault(c => c.Name == actionName && c.Name != "start")
            ?? throw new KeyNotFoundException($"ação \"{actionName}\" não encontrada em \"{projectId}\"");

        if (snapshot.Status != TrustStatus.Approved)
        {
            throw new ManifestNotApprovedException(
                $"projeto \"{projectId}\" está com status {snapshot.Status} — aprove no Admin antes de executar ações");
        }
        if (command.Interactive)
        {
            throw new ActionInteractiveException($"ação \"{actionName}\" é interativa, não suportada via API");
        }
        if (command.Destructive && !confirmed)
        {
            throw new ConfirmationRequiredException($"ação \"{actionName}\" é destrutiva — exige confirm=true");
        }

        return CommandExecutor.Run(command);
    }

    private IAdapter GetAdapter(string projectId) =>
        _adapters.GetOrAdd(projectId, id => AdapterFactory.Create(registry.Get(id)));
}
