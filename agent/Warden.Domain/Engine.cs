using System.Threading.Channels;
using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Warden.Domain.Discovery;
using Warden.Domain.Events;
using Warden.Domain.Languages;
using Warden.Domain.Notify;
using Warden.Domain.Trust;
using Warden.Domain.Vitals;
using Warden.Domain.Watch;

namespace Warden.Domain;

/// <summary>
/// Facade que coordena Registry + AdapterPool + ManifestRegistry + WatcherCoordinator + EventBus.
/// Cada responsabilidade interna vive na sua própria classe; o Engine só orquestra o fluxo de alto
/// nível (equivalente ao `Engine` do engine Python — ver NEW_CONTEXT.md §12 fase 8).
/// </summary>
public sealed class Engine
{
    private readonly Registry _registry;
    private readonly ManifestRegistry _manifestRegistry;
    private readonly IEventStore? _store;
    private readonly INotifier _notifier;
    private readonly AdapterPool _adapterPool;
    private readonly WatcherCoordinator _watchers;
    private readonly SystemVitalsSampler _systemVitalsSampler = new();
    private ProjectsWatcher? _projectsWatcher;

    public EventBus Bus { get; } = new();

    public Engine(Registry registry, ManifestRegistry manifestRegistry, IEventStore? store = null, INotifier? notifier = null)
    {
        _registry = registry;
        _manifestRegistry = manifestRegistry;
        _store = store;
        _notifier = notifier ?? new NullNotifier();

        _adapterPool = new AdapterPool(_registry);
        _adapterPool.SetOnExit((projectId, returnCode) =>
        {
            var type = returnCode == 0 ? EventType.Finished : EventType.Error;
            Bus.Publish(new Event(projectId, type, $"exit={returnCode}"));
        });

        _watchers = new WatcherCoordinator(Bus.Publish,
            (projectId, actionName) => RunAction(projectId, actionName, confirmed: false));

        if (_store is not null) Bus.Subscribe(_store.Record);
        Bus.Subscribe(MaybeNotify);
    }

    public void Boot(TimeSpan? projectsWatchInterval = null)
    {
        _registry.Load();
        foreach (var project in _registry.All())
            _manifestRegistry.Refresh(project);

        _watchers.StartAll(_registry.All());
        _projectsWatcher = new ProjectsWatcher(_registry.ProjectsDir, ReloadRegistry, projectsWatchInterval);
        _projectsWatcher.Start();
        _systemVitalsSampler.Prime();
    }

    /// <summary>
    /// Shutdown gracioso do daemon. <see cref="ReloadRegistry"/> chama <see cref="WatcherCoordinator.Restart"/>
    /// diretamente — não este método — para evitar que o callback do ProjectsWatcher tente dar Join
    /// nele mesmo.
    /// </summary>
    public void Shutdown()
    {
        _projectsWatcher?.Stop();
        _projectsWatcher = null;
        _watchers.StopAll();
    }

    public IReadOnlyList<ProjectConfig> AllProjects() => _registry.All();

    public ProjectConfig GetProject(string projectId) => _registry.Get(projectId);

    /// <summary>Start passa pelo trust gate (§10.3/10.4) — o comando [start] faz parte do manifesto.</summary>
    public void Start(string projectId)
    {
        if (Manifest(projectId).Status != TrustStatus.Approved)
            throw new ManifestNotApprovedException(
                $"projeto \"{projectId}\" não está aprovado — aprove no Admin antes de iniciar");

        _adapterPool.Get(projectId).Start();
        Bus.Publish(new Event(projectId, EventType.Started));
    }

    public void Stop(string projectId)
    {
        _adapterPool.Get(projectId).Stop();
        Bus.Publish(new Event(projectId, EventType.Stopped));
    }

    public ProcessStatus Status(string projectId) => _adapterPool.Get(projectId).Status();

    public IReadOnlyList<string> Logs(string projectId, int tail = 100, string? service = null) =>
        _adapterPool.Get(projectId).Logs(tail, service);

    /// <summary>
    /// Retorna um <see cref="ChannelReader{T}"/> que emite linhas em tempo real conforme chegam do
    /// processo. Null quando o adapter não suporta streaming (ex: Docker) — o Hub deve fazer fallback
    /// para polling via <see cref="Logs"/>. O reader é fechado quando o <paramref name="cancellation"/>
    /// é cancelado (desconexão do Hub) ou quando o processo termina.
    /// </summary>
    public ChannelReader<string>? SubscribeLogs(string projectId, CancellationToken cancellation) =>
        _adapterPool.Get(projectId).LogBroadcaster?.Subscribe(cancellation);

    public IReadOnlyList<string> Services(string projectId) => _adapterPool.Get(projectId).Services();

    public ProjectManifestSnapshot Manifest(string projectId) =>
        _manifestRegistry.Get(projectId)
        ?? throw new KeyNotFoundException($"manifesto do projeto \"{projectId}\" ainda não foi resolvido");

    /// <summary>Aprovação só por caminho local/confiável (Admin). Sem rota HTTP no Console — ver NEW_CONTEXT.md §10.3.</summary>
    public ProjectManifestSnapshot Approve(string projectId) => _manifestRegistry.Approve(GetProject(projectId));

    public ActionExecutionResult RunAction(string projectId, string actionName, bool confirmed)
    {
        var snapshot = Manifest(projectId);
        var command = snapshot.Manifest.Commands.FirstOrDefault(c => c.Name == actionName && c.Name != "start")
            ?? throw new KeyNotFoundException($"ação \"{actionName}\" não encontrada em \"{projectId}\"");

        if (snapshot.Status != TrustStatus.Approved)
            throw new ManifestNotApprovedException(
                $"projeto \"{projectId}\" está com status {snapshot.Status} — aprove no Admin antes de executar ações");

        if (command.Interactive)
            throw new ActionInteractiveException($"ação \"{actionName}\" é interativa, não suportada via API");

        if (command.Destructive && !confirmed)
            throw new ConfirmationRequiredException($"ação \"{actionName}\" é destrutiva — exige confirm=true");

        var result = CommandExecutor.Run(command);
        if (command.Destructive)
            _store?.RecordAction(projectId, actionName, command.Argv, confirmed, result.ExitCode);

        return result;
    }

    public SystemVitalsInfo SystemVitals() => _systemVitalsSampler.Sample();

    public IReadOnlyList<string> Languages(string projectId) =>
        LanguageDetector.Detect(GetProject(projectId).Path);

    public Git.GitInfo? GitInfo(string projectId) => Git.GitService.Info(GetProject(projectId).Path);

    /// <summary>Verbos mutantes (pull/push) exigem confirmed=true — mesma semântica 409 das ações destrutivas.</summary>
    public Git.GitCommandResult GitCommand(string projectId, string verb, bool confirmed = false)
    {
        var project = GetProject(projectId);
        if (Git.GitService.ConfirmVerbs.Contains(verb) && !confirmed)
            throw new ConfirmationRequiredException($"verbo git \"{verb}\" exige confirmação");

        return Git.GitService.Command(project.Path, verb, project.Git.Remote);
    }

    public IReadOnlyList<HistoryEntry> History(string projectId, int limit = 50) =>
        _store?.History(projectId, limit) ?? [];

    public IReadOnlyList<ActionAuditEntry> ActionAudit(string projectId, int limit = 50) =>
        _store?.ActionAudit(projectId, limit) ?? [];

    // ---- Discovery / Scaffold ----

    private string GlobalConfigPath => Path.Combine(_registry.ConfigDir, "config.toml");

    private string ProjectTomlPath(string projectId) =>
        Path.Combine(_registry.ConfigDir, "projects", $"{projectId}.toml");

    public GlobalConfig LoadGlobalConfig() => ConfigLoader.LoadGlobalConfig(GlobalConfigPath);

    public GlobalConfig AddScanPath(string path) => ProjectDiscovery.AddScanPath(_registry.ConfigDir, path);

    public GlobalConfig RemoveScanPath(string path) => ProjectDiscovery.RemoveScanPath(_registry.ConfigDir, path);

    public IReadOnlyList<DiscoveredProject> Discover() => ProjectDiscovery.DiscoverProjects(LoadGlobalConfig(), _registry);

    public BrowseResult Browse(string? path) => ProjectDiscovery.BrowseDirectory(path);

    public ProjectConfig PreviewConfig(string path, string? projectId) => Scaffold.BuildConfig(path, projectId);

    public ProjectConfig GetProjectConfigFile(string projectId)
    {
        var target = ProjectTomlPath(projectId);
        if (!File.Exists(target))
            throw new KeyNotFoundException($"config de \"{projectId}\" não encontrada");

        return ConfigLoader.LoadProjectConfig(target);
    }

    public string ApplyConfig(ProjectConfig config)
    {
        var toml = Scaffold.RenderToml(config);
        var target = ProjectTomlPath(config.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, toml);
        ReloadRegistry();
        return toml;
    }

    /// <summary>
    /// Recarrega config do disco sem restart do daemon. Preserva adapters de processos rodando para não
    /// perder referência ao processo em execução.
    /// </summary>
    public void ReloadRegistry()
    {
        _registry.Load();
        foreach (var project in _registry.All())
            _manifestRegistry.Refresh(project);

        _adapterPool.PruneStopped();
        _watchers.Restart(_registry.All());
    }

    private void MaybeNotify(Event @event)
    {
        ProjectConfig project;
        try { project = _registry.Get(@event.ProjectId); }
        catch (KeyNotFoundException) { return; }

        var shouldNotify = @event.Type switch
        {
            EventType.Error => project.Notify.OnError,
            EventType.Finished => project.Notify.OnFinished,
            EventType.GitBehind => project.Notify.OnGitBehind,
            _ => false,
        };
        if (shouldNotify) _notifier.Notify(@event, project);
    }
}
