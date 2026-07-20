namespace Warden.Domain.Watch;

/// <summary>
/// Poll de `~/.warden/projects/*.toml` -> recarrega registry quando algo muda. Sem isso, criar/editar/
/// remover um `.toml` com o daemon já rodando só aparecia depois de reiniciar. Poll de mtime é simples
/// o bastante pra não precisar de um watcher de filesystem nativo — mesmo padrão de thread própria do
/// <see cref="Git.GitWatcher"/>/<see cref="FileErrorWatcher"/>. Mirror de `projects_watcher.py`.
/// </summary>
public sealed class ProjectsWatcher
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly string _projectsDir;
    private readonly Action _onChange;
    private readonly TimeSpan _interval;
    private readonly ManualResetEventSlim _stopSignal = new(false);

    private Thread? _thread;
    private HashSet<(string Name, DateTime MTime)> _lastSnapshot;

    public ProjectsWatcher(string projectsDir, Action onChange, TimeSpan? interval = null)
    {
        _projectsDir = projectsDir;
        _onChange = onChange;
        _interval = interval ?? DefaultInterval;
        _lastSnapshot = Snapshot(_projectsDir);
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stopSignal.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        while (!_stopSignal.IsSet)
        {
            PollOnce();
            _stopSignal.Wait(_interval);
        }
    }

    private void PollOnce()
    {
        var current = Snapshot(_projectsDir);
        if (!current.SetEquals(_lastSnapshot))
        {
            _lastSnapshot = current;
            _onChange();
        }
    }

    private static HashSet<(string, DateTime)> Snapshot(string projectsDir)
    {
        if (!Directory.Exists(projectsDir)) return [];
        return Directory.GetFiles(projectsDir, "*.toml")
            .Select(f => (Path.GetFileName(f), File.GetLastWriteTimeUtc(f)))
            .ToHashSet();
    }
}
