using System.Collections.Concurrent;
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
/// Facade que amarra <see cref="Registry"/> + <see cref="AdapterFactory"/> + <see cref="ManifestRegistry"/>
/// + <see cref="EventBus"/> pro Agent consumir — equivalente ao `Engine` do engine Python (mirror de
/// `engine.py`, ver NEW_CONTEXT.md §12 fase 8). `store`/`notifier` são opcionais (mesmo default de
/// `Engine.__init__` do Python) — sem eles o bus ainda publica, só não persiste nem notifica.
/// </summary>
public sealed class Engine
{
    private readonly Registry _registry;
    private readonly ManifestRegistry _manifestRegistry;
    private readonly IEventStore? _store;
    private readonly INotifier _notifier;
    private readonly ConcurrentDictionary<string, IAdapter> _adapters = new();
    private readonly SystemVitalsSampler _systemVitalsSampler = new();
    private readonly object _watchersGate = new();
    private readonly List<Git.GitWatcher> _gitWatchers = [];
    private readonly List<FileErrorWatcher> _fileWatchers = [];
    private ProjectsWatcher? _projectsWatcher;

    public EventBus Bus { get; } = new();

    public Engine(Registry registry, ManifestRegistry manifestRegistry, IEventStore? store = null, INotifier? notifier = null)
    {
        _registry = registry;
        _manifestRegistry = manifestRegistry;
        _store = store;
        _notifier = notifier ?? new NullNotifier();

        if (_store is not null) Bus.Subscribe(_store.Record);
        Bus.Subscribe(MaybeNotify);
    }

    public void Boot(TimeSpan? projectsWatchInterval = null)
    {
        _registry.Load();
        foreach (var project in _registry.All())
        {
            _manifestRegistry.Refresh(project);
        }
        StartGitWatchers();
        StartFileWatchers();
        _projectsWatcher = new ProjectsWatcher(_registry.ProjectsDir, ReloadRegistry, projectsWatchInterval);
        _projectsWatcher.Start();
        _systemVitalsSampler.Prime();
    }

    /// <summary>
    /// Shutdown completo — chamado em desligamento gracioso do daemon. Note que <see cref="ReloadRegistry"/>
    /// usa <see cref="StopWatchers"/> diretamente, não este método: parar o <see cref="_projectsWatcher"/> de
    /// dentro do seu próprio callback (`on_change` → `ReloadRegistry`) faria a thread dele tentar dar `Join`
    /// nela mesma e travar.
    /// </summary>
    public void Shutdown()
    {
        _projectsWatcher?.Stop();
        _projectsWatcher = null;
        StopWatchers();
    }

    private void StopWatchers()
    {
        lock (_watchersGate)
        {
            foreach (var watcher in _gitWatchers) watcher.Stop();
            _gitWatchers.Clear();
            foreach (var watcher in _fileWatchers) watcher.Stop();
            _fileWatchers.Clear();
        }
    }

    public IReadOnlyList<ProjectConfig> AllProjects() => _registry.All();

    public ProjectConfig GetProject(string projectId) => _registry.Get(projectId);

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
        Bus.Publish(new Event(projectId, EventType.Started));
    }

    public void Stop(string projectId)
    {
        GetAdapter(projectId).Stop();
        Bus.Publish(new Event(projectId, EventType.Stopped));
    }

    public ProcessStatus Status(string projectId) => GetAdapter(projectId).Status();

    public IReadOnlyList<string> Logs(string projectId, int tail = 100, string? service = null) =>
        GetAdapter(projectId).Logs(tail, service);

    public IReadOnlyList<string> Services(string projectId) => GetAdapter(projectId).Services();

    public ProjectManifestSnapshot Manifest(string projectId) =>
        _manifestRegistry.Get(projectId)
        ?? throw new KeyNotFoundException($"manifesto do projeto \"{projectId}\" ainda não foi resolvido");

    /// <summary>
    /// Passthrough pro <see cref="ManifestRegistry.Approve"/> — chamada só por caminho local/confiável
    /// (hoje: testes; a partir da fase 6, o handler IPC do Admin). Não existe rota HTTP pra isso na
    /// Plateia/Console, de propósito (ver NEW_CONTEXT.md §10.3).
    /// </summary>
    public ProjectManifestSnapshot Approve(string projectId) => _manifestRegistry.Approve(GetProject(projectId));

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

        var result = CommandExecutor.Run(command);
        if (command.Destructive)
        {
            _store?.RecordAction(projectId, actionName, command.Argv, confirmed, result.ExitCode);
        }
        return result;
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

    /// <summary>Histórico de eventos (started/stopped/finished/error/git_behind) — [] se não houver `IEventStore` configurado.</summary>
    public IReadOnlyList<HistoryEntry> History(string projectId, int limit = 50) =>
        _store?.History(projectId, limit) ?? [];

    /// <summary>Audit de ações destrutivas já executadas — [] se não houver `IEventStore` configurado.</summary>
    public IReadOnlyList<ActionAuditEntry> ActionAudit(string projectId, int limit = 50) =>
        _store?.ActionAudit(projectId, limit) ?? [];

    private void MaybeNotify(Event @event)
    {
        ProjectConfig project;
        try
        {
            project = _registry.Get(@event.ProjectId);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        var shouldNotify = @event.Type switch
        {
            EventType.Error => project.Notify.OnError,
            EventType.Finished => project.Notify.OnFinished,
            EventType.GitBehind => project.Notify.OnGitBehind,
            _ => false,
        };
        if (shouldNotify) _notifier.Notify(@event, project);
    }

    private void StartGitWatchers()
    {
        lock (_watchersGate)
        {
            foreach (var project in _registry.All())
            {
                if (!project.Git.Watch) continue;

                var projectId = project.Id;
                var remote = project.Git.Remote;
                var watcher = new Git.GitWatcher(project.Path, remote, project.Git.Interval, behind =>
                    Bus.Publish(new Event(projectId, EventType.GitBehind, $"{behind} commit(s) atrás de {remote}")));
                watcher.Start();
                _gitWatchers.Add(watcher);
            }
        }
    }

    private void HandleExit(string projectId, int returnCode)
    {
        var type = returnCode == 0 ? EventType.Finished : EventType.Error;
        Bus.Publish(new Event(projectId, type, $"exit={returnCode}"));
    }

    private IAdapter GetAdapter(string projectId) =>
        _adapters.GetOrAdd(projectId, id =>
        {
            var adapter = AdapterFactory.Create(_registry.Get(id));
            adapter.SetOnExit(returnCode => HandleExit(id, returnCode));
            return adapter;
        });

    // ---- Descoberta/sincronização de projetos (mirror de discovery.py/scaffold.py) ----

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
        {
            throw new KeyNotFoundException($"config de \"{projectId}\" não encontrada");
        }
        return ConfigLoader.LoadProjectConfig(target);
    }

    /// <summary>Grava o `.toml` do projeto (novo ou edição) e recarrega a registry — mesmo par "warden init web" + reload do Python.</summary>
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
    /// Recarrega config do disco sem restart do daemon. Invalida (remove do cache) só os adapters de
    /// projetos **parados** — preserva o de um projeto rodando, senão a próxima chamada recriaria um
    /// adapter novo sem PID/processo associado e "perderia" o processo já em execução. Mesmo fix que o
    /// Python aplicou depois de achar esse bug real (ver NEW_CONTEXT.md, fase de vitals). Reinicia os
    /// git watchers pra pegar `[git] watch` novo/alterado — mesmo padrão do `reload_registry` Python.
    /// </summary>
    public void ReloadRegistry()
    {
        _registry.Load();
        foreach (var project in _registry.All())
        {
            _manifestRegistry.Refresh(project);
        }
        foreach (var projectId in _adapters.Keys.ToList())
        {
            if (_adapters.TryGetValue(projectId, out var adapter) && !adapter.Status().Running)
            {
                _adapters.TryRemove(projectId, out _);
            }
        }

        StopWatchers();
        StartGitWatchers();
        StartFileWatchers();
    }

    private void StartFileWatchers()
    {
        lock (_watchersGate)
        {
            foreach (var project in _registry.All())
            {
                foreach (var logSource in project.LogSources)
                {
                    if (logSource.Type != "file" || logSource.ErrorPatterns.Count == 0) continue;

                    var projectId = project.Id;
                    var sourceName = logSource.Name;
                    var watcher = new FileErrorWatcher(
                        ResolveLogPath(project, logSource.Path),
                        logSource.ErrorPatterns,
                        entry => Bus.Publish(new Event(projectId, EventType.Error, $"[{sourceName}] {entry}")));
                    watcher.Start();
                    _fileWatchers.Add(watcher);
                }
            }
        }
    }

    private static string ResolveLogPath(ProjectConfig project, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(logPath);
        return Path.IsPathRooted(logPath) ? logPath : Path.Combine(project.Path, logPath);
    }
}
