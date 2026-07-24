namespace Warden.Domain.Watch;

/// <summary>
/// Verifica a cada minuto se a expressão cron bate com o horário UTC atual e, se sim, invoca o
/// callback de execução. Segue o mesmo padrão de <see cref="Git.GitWatcher"/>: thread background,
/// <see cref="ManualResetEventSlim"/> como sinal de parada. O clock é injetável para testes.
/// </summary>
public sealed class CronActionWatcher
{
    private readonly string _projectId;
    private readonly string _actionName;
    private readonly CronSchedule _schedule;
    private readonly Action _execute;
    private readonly Func<DateTime> _clock;
    private readonly ManualResetEventSlim _stopSignal = new(false);

    private Thread? _thread;

    public string ProjectId => _projectId;
    public string ActionName => _actionName;

    public CronActionWatcher(
        string projectId,
        string actionName,
        CronSchedule schedule,
        Action execute)
        : this(projectId, actionName, schedule, execute, () => DateTime.UtcNow)
    {
    }

    public CronActionWatcher(
        string projectId,
        string actionName,
        CronSchedule schedule,
        Action execute,
        Func<DateTime> clock)
    {
        _projectId = projectId;
        _actionName = actionName;
        _schedule = schedule;
        _execute = execute;
        _clock = clock;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = $"cron:{_projectId}/{_actionName}" };
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
            // Alinha com o início do próximo minuto
            var now = _clock();
            var msUntilNextMinute = (60 - now.Second) * 1000 - now.Millisecond;
            if (_stopSignal.Wait(msUntilNextMinute)) break;

            if (_schedule.Matches(_clock()))
            {
                try { _execute(); }
                catch { /* exceções são tratadas no callback do WatcherCoordinator */ }
            }
        }
    }
}
