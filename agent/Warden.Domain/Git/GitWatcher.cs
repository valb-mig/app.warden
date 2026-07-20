namespace Warden.Domain.Git;

/// <summary>
/// Fetch periódico → detecta drift (commits novos no origin não puxados). Só fetch (read-only, não
/// mexe em working tree). Notifica uma vez na transição pra "atrás do origin", não a cada poll —
/// evita spam enquanto o usuário não agiu. Mirror de `git_watcher.py`.
/// </summary>
public sealed class GitWatcher
{
    private readonly string _path;
    private readonly string _remote;
    private readonly TimeSpan _interval;
    private readonly Action<int> _onBehind;
    private readonly ManualResetEventSlim _stopSignal = new(false);

    private Thread? _thread;
    private int _lastBehind;

    public GitWatcher(string path, string remote, double intervalSeconds, Action<int> onBehind)
    {
        _path = path;
        _remote = remote;
        _interval = TimeSpan.FromSeconds(intervalSeconds);
        _onBehind = onBehind;
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
        var fetch = GitService.Command(_path, "fetch", _remote);
        if (!fetch.Ok) return; // rede instável / sem credencial — próximo poll cobre

        var info = GitService.Info(_path);
        if (info is null) return;

        var behind = info.Behind ?? 0;
        if (behind > 0 && _lastBehind == 0) _onBehind(behind);
        _lastBehind = behind;
    }
}
