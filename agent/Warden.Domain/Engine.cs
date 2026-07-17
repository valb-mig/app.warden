using System.Collections.Concurrent;
using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Warden.Domain.Languages;
using Warden.Domain.Trust;
using Warden.Domain.Vitals;

namespace Warden.Domain;

/// <summary>
/// Facade que amarra <see cref="Registry"/> + <see cref="AdapterFactory"/> + <see cref="ManifestRegistry"/>
/// pro Agent consumir — equivalente ao `Engine` do engine Python, mas escopado só ao que já foi
/// portado (bus/notifier/watchers/history/languages ainda ficam pra fase 8, git de leitura+comandos
/// já entrou — ver NEW_CONTEXT.md §12 fase 8).
/// </summary>
public sealed class Engine(Registry registry, ManifestRegistry manifestRegistry)
{
    private readonly ConcurrentDictionary<string, IAdapter> _adapters = new();
    private readonly SystemVitalsSampler _systemVitalsSampler = new();

    public void Boot()
    {
        registry.Load();
        foreach (var project in registry.All())
        {
            manifestRegistry.Refresh(project);
        }
        _systemVitalsSampler.Prime();
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

    public SystemVitalsInfo SystemVitals() => _systemVitalsSampler.Sample();

    public IReadOnlyList<string> Languages(string projectId) =>
        LanguageDetector.Detect(GetProject(projectId).Path);

    public Git.GitInfo? GitInfo(string projectId) => Git.GitService.Info(GetProject(projectId).Path);

    /// <summary>
    /// Verbos mutantes (pull/push) exigem <c>confirmed=true</c> — mesma semântica 409 das ações
    /// destrutivas, checada aqui e não dentro do <see cref="Git.GitService"/> (que fica agnóstico
    /// de HTTP/confirmação, só de allowlist/guardas de working tree).
    /// </summary>
    public Git.GitCommandResult GitCommand(string projectId, string verb, bool confirmed = false)
    {
        var project = GetProject(projectId);
        if (Git.GitService.ConfirmVerbs.Contains(verb) && !confirmed)
        {
            throw new ConfirmationRequiredException($"verbo git \"{verb}\" exige confirmação");
        }
        return Git.GitService.Command(project.Path, verb, project.Git.Remote);
    }

    private IAdapter GetAdapter(string projectId) =>
        _adapters.GetOrAdd(projectId, id => AdapterFactory.Create(registry.Get(id)));
}
