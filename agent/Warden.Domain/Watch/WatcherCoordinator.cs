using Warden.Domain.Config;
using Warden.Domain.Events;

namespace Warden.Domain.Watch;

/// <summary>
/// Coordena o ciclo de vida dos watchers de Git, arquivo e cron por projeto. Separado do Engine
/// para que Start/Stop/Reload dos watchers não polua a facade de domínio.
/// </summary>
public sealed class WatcherCoordinator
{
    private readonly object _gate = new();
    private readonly List<Git.GitWatcher> _gitWatchers = [];
    private readonly List<FileErrorWatcher> _fileWatchers = [];
    private readonly List<CronActionWatcher> _cronWatchers = [];
    private readonly Action<Event> _publish;
    private readonly Action<string, string> _runAction;

    /// <param name="publish">Callback para publicar eventos no bus.</param>
    /// <param name="runAction">Callback para executar uma action: (projectId, actionName). Deve jogar exceção em caso de falha.</param>
    public WatcherCoordinator(Action<Event> publish, Action<string, string> runAction)
    {
        _publish = publish;
        _runAction = runAction;
    }

    public void StartAll(IReadOnlyList<ProjectConfig> projects)
    {
        lock (_gate)
        {
            StartGitWatchers(projects);
            StartFileWatchers(projects);
            StartCronWatchers(projects);
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var w in _gitWatchers) w.Stop();
            _gitWatchers.Clear();
            foreach (var w in _fileWatchers) w.Stop();
            _fileWatchers.Clear();
            foreach (var w in _cronWatchers) w.Stop();
            _cronWatchers.Clear();
        }
    }

    public void Restart(IReadOnlyList<ProjectConfig> projects)
    {
        StopAll();
        lock (_gate)
        {
            StartGitWatchers(projects);
            StartFileWatchers(projects);
            StartCronWatchers(projects);
        }
    }

    private void StartGitWatchers(IReadOnlyList<ProjectConfig> projects)
    {
        foreach (var project in projects)
        {
            if (!project.Git.Watch) continue;

            var projectId = project.Id;
            var remote = project.Git.Remote;
            var watcher = new Git.GitWatcher(project.Path, remote, project.Git.Interval, behind =>
                _publish(new Event(projectId, EventType.GitBehind, $"{behind} commit(s) atrás de {remote}")));
            watcher.Start();
            _gitWatchers.Add(watcher);
        }
    }

    private void StartFileWatchers(IReadOnlyList<ProjectConfig> projects)
    {
        foreach (var project in projects)
        {
            foreach (var logSource in project.LogSources)
            {
                if (logSource.Type != "file" || logSource.ErrorPatterns.Count == 0) continue;

                var projectId = project.Id;
                var sourceName = logSource.Name;
                var logPath = ResolveLogPath(project, logSource.Path);
                var watcher = new FileErrorWatcher(
                    logPath,
                    logSource.ErrorPatterns,
                    entry => _publish(new Event(projectId, EventType.Error, $"[{sourceName}] {entry}")));
                watcher.Start();
                _fileWatchers.Add(watcher);
            }
        }
    }

    private void StartCronWatchers(IReadOnlyList<ProjectConfig> projects)
    {
        foreach (var project in projects)
        {
            foreach (var action in project.Actions)
            {
                if (action.Cron is null) continue;
                if (action.Interactive || action.Destructive) continue; // sem auto-confirm nem sessão interativa

                if (!CronSchedule.TryParse(action.Cron, out var schedule) || schedule is null)
                    continue; // expressão inválida — ignorada silenciosamente

                var projectId = project.Id;
                var actionName = action.Name;
                var watcher = new CronActionWatcher(projectId, actionName, schedule, () =>
                {
                    try
                    {
                        _runAction(projectId, actionName);
                        _publish(new Event(projectId, EventType.CronAction, $"cron: {actionName} executou com sucesso"));
                    }
                    catch (Exception ex)
                    {
                        _publish(new Event(projectId, EventType.Error, $"cron: {actionName} falhou — {ex.Message}"));
                    }
                });
                watcher.Start();
                _cronWatchers.Add(watcher);
            }
        }
    }

    private static string ResolveLogPath(ProjectConfig project, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(logPath);
        return Path.IsPathRooted(logPath) ? logPath : Path.Combine(project.Path, logPath);
    }
}
