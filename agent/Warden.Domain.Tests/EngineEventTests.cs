using System.Diagnostics;
using Warden.Domain.Config;
using Warden.Domain.Events;
using Warden.Domain.Notify;
using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Mirror das seções de evento/notifier/audit de `engine/tests/test_engine.py` — spawn real de
/// `python3`, SQLite real, git real, nada mockado. `FileErrorWatcher`/`ProjectsWatcher` (também
/// cobertos no arquivo Python) não foram portados nesta fatia, então os testes correspondentes
/// ficam de fora aqui de propósito.
/// </summary>
public sealed class EngineEventTests : IDisposable
{
    private readonly string _configDir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_configDir, recursive: true);

    private void WriteProject(string projectId, IReadOnlyList<string> cmd, bool notifyOnError = false, string extraToml = "")
    {
        var projectsDir = Directory.CreateDirectory(Path.Combine(_configDir, "projects")).FullName;
        var cmdToml = "[" + string.Join(", ", cmd.Select(c => $"\"{c}\"")) + "]";
        var notify = notifyOnError ? "\n[notify]\non_error = true\n" : "";
        File.WriteAllText(
            Path.Combine(projectsDir, $"{projectId}.toml"),
            $"id = \"{projectId}\"\ntype = \"raw\"\npath = \"{_configDir}\"\n\n[start]\ncmd = {cmdToml}\n{notify}{extraToml}");
    }

    private void WriteProjectWithAction(string projectId, string actionToml, string path)
    {
        var projectsDir = Directory.CreateDirectory(Path.Combine(_configDir, "projects")).FullName;
        File.WriteAllText(
            Path.Combine(projectsDir, $"{projectId}.toml"),
            $"id = \"{projectId}\"\ntype = \"raw\"\npath = \"{path}\"\n\n[start]\ncmd = [\"true\"]\n\n{actionToml}");
    }

    private (Engine Engine, List<Event> Events) EngineWithStore(INotifier? notifier = null)
    {
        var registry = new Registry(_configDir);
        var trustStore = new SqliteTrustStore(Path.Combine(_configDir, "warden.db"));
        var manifestRegistry = new ManifestRegistry(trustStore);
        var eventStore = new SqliteEventStore(Path.Combine(_configDir, "warden.db"));
        var engine = new Engine(registry, manifestRegistry, eventStore, notifier);
        engine.Boot();
        var events = new List<Event>();
        engine.Bus.Subscribe(events.Add);
        return (engine, events);
    }

    private static void WaitUntil(Func<bool> predicate, double timeoutSeconds = 5.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            Thread.Sleep(50);
        }
        Assert.Fail("condição não satisfeita dentro do timeout");
    }

    [Fact]
    public void StartAndStopEmitEventsWithoutFinished()
    {
        WriteProject("demo", ["python3", "-c", "import time; time.sleep(30)"]);
        var (engine, events) = EngineWithStore();
        engine.Approve("demo");

        engine.Start("demo");
        WaitUntil(() => engine.Status("demo").Running);
        engine.Stop("demo");
        WaitUntil(() => !engine.Status("demo").Running);
        Thread.Sleep(300); // dá tempo pra evento errado disparar, se houver bug

        Assert.Equal([EventType.Started, EventType.Stopped], events.Select(e => e.Type));
    }

    [Fact]
    public void ProcessFinishingOnItsOwnEmitsFinished()
    {
        WriteProject("demo", ["python3", "-c", "pass"]);
        var (engine, events) = EngineWithStore();
        engine.Approve("demo");

        engine.Start("demo");
        WaitUntil(() => events.Any(e => e.Type == EventType.Finished));

        Assert.Equal([EventType.Started, EventType.Finished], events.Select(e => e.Type));
        var history = engine.History("demo");
        Assert.Equal("finished", history[0].Type);
    }

    [Fact]
    public void ProcessErroringOnItsOwnEmitsError()
    {
        WriteProject("demo", ["python3", "-c", "import sys; sys.exit(1)"]);
        var (engine, events) = EngineWithStore();
        engine.Approve("demo");

        engine.Start("demo");
        WaitUntil(() => events.Any(e => e.Type == EventType.Error));

        Assert.Equal([EventType.Started, EventType.Error], events.Select(e => e.Type));
    }

    [Fact]
    public void NotifierCalledOnlyWhenProjectOptsIn()
    {
        WriteProject("notify-on", ["python3", "-c", "import sys; sys.exit(1)"], notifyOnError: true);
        WriteProject("notify-off", ["python3", "-c", "import sys; sys.exit(1)"]);
        var notifier = new FakeNotifier();
        var (engine, _) = EngineWithStore(notifier);
        engine.Approve("notify-on");
        engine.Approve("notify-off");

        engine.Start("notify-on");
        engine.Start("notify-off");
        WaitUntil(() => notifier.Calls.Count >= 1);
        Thread.Sleep(300);

        Assert.Equal(["notify-on"], notifier.Calls.Select(e => e.ProjectId));
    }

    [Fact]
    public void DestructiveActionConfirmedRunsAndAudits()
    {
        WriteProjectWithAction("demo", "[[actions]]\nname = \"wipe\"\ncmd = [\"echo\", \"wiped\"]\ndestructive = true\n", _configDir);
        var (engine, _) = EngineWithStore();
        engine.Approve("demo");

        var result = engine.RunAction("demo", "wipe", confirmed: true);

        Assert.Equal(0, result.ExitCode);
        var audit = engine.ActionAudit("demo");
        Assert.Single(audit);
        Assert.Equal("wipe", audit[0].ActionName);
        Assert.True(audit[0].Confirmed);
        Assert.Equal(["echo", "wiped"], audit[0].Cmd);
    }

    [Fact]
    public void NonDestructiveActionSkipsAudit()
    {
        WriteProjectWithAction("demo", "[[actions]]\nname = \"hello\"\ncmd = [\"echo\", \"hi\"]\n", _configDir);
        var (engine, _) = EngineWithStore();
        engine.Approve("demo");

        engine.RunAction("demo", "hello", confirmed: false);

        Assert.Empty(engine.ActionAudit("demo"));
    }

    private static void RunGit(string path, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(path);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void GitWatcherEmitsGitBehindAndRespectsNotifyOptIn()
    {
        var remote = Path.Combine(_configDir, "remote.git");
        Directory.CreateDirectory(remote);
        RunGit(remote, "init", "--bare", "-b", "main");

        var up = Path.Combine(_configDir, "up");
        Directory.CreateDirectory(up);
        RunGit(up, "init", "-b", "main");
        RunGit(up, "config", "user.email", "up@warden.local");
        RunGit(up, "config", "user.name", "Up");
        File.WriteAllText(Path.Combine(up, "a.txt"), "x");
        RunGit(up, "add", "a.txt");
        RunGit(up, "commit", "-m", "base");
        RunGit(up, "remote", "add", "origin", remote);
        RunGit(up, "push", "-u", "origin", "main");

        var down = Path.Combine(_configDir, "down");
        RunGit(_configDir, "clone", remote, down);
        RunGit(down, "config", "user.email", "down@warden.local");
        RunGit(down, "config", "user.name", "Down");

        WriteProjectWithAction("demo", "[git]\nwatch = true\ninterval = 0.2\n", down);
        var notifier = new FakeNotifier();
        var (engine, events) = EngineWithStore(notifier);

        try
        {
            File.WriteAllText(Path.Combine(up, "b.txt"), "novo");
            RunGit(up, "add", "b.txt");
            RunGit(up, "commit", "-m", "novo commit");
            RunGit(up, "push", "origin", "main");

            WaitUntil(() => events.Any(e => e.Type == EventType.GitBehind), timeoutSeconds: 6);
            Assert.Empty(notifier.Calls); // notify.on_git_behind não foi ligado neste projeto
        }
        finally
        {
            engine.Shutdown();
        }
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<Event> Calls { get; } = [];
        public void Notify(Event @event, ProjectConfig project) => Calls.Add(@event);
    }
}
