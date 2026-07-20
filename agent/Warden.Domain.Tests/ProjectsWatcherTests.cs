using Warden.Domain.Events;
using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Mirror de `test_projects_watcher_picks_up_new_toml_without_manual_reload` /
/// `..._edit_to_stopped_project` em `engine/tests/test_engine.py` — o `ProjectsWatcher` em si é só
/// poll de mtime (testado implicitamente aqui), o valor real está em provar que o `Engine` reage
/// sozinho sem chamar `ReloadRegistry` manualmente.
/// </summary>
public sealed class ProjectsWatcherTests : IDisposable
{
    private readonly string _configDir = Directory.CreateTempSubdirectory().FullName;
    private Engine? _engine;

    public void Dispose()
    {
        try
        {
            _engine?.Shutdown();
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private Engine BootEngine()
    {
        var registry = new Registry(_configDir);
        var trustStore = new SqliteTrustStore(Path.Combine(_configDir, "warden.db"));
        var manifestRegistry = new ManifestRegistry(trustStore);
        _engine = new Engine(registry, manifestRegistry);
        _engine.Boot(TimeSpan.FromMilliseconds(50));
        return _engine;
    }

    private void WriteProject(string projectId, string cmdShellLine)
    {
        var projectsDir = Directory.CreateDirectory(Path.Combine(_configDir, "projects"));
        var toml = $"""
            id = "{projectId}"
            type = "raw"
            path = "{_configDir}"

            [start]
            cmd = ["bash", "-c", "{cmdShellLine}"]
            """;
        File.WriteAllText(Path.Combine(projectsDir.FullName, $"{projectId}.toml"), toml);
    }

    private static void WaitUntil(Func<bool> predicate, double timeoutSeconds = 6.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            Thread.Sleep(100);
        }
        Assert.Fail("condição não satisfeita dentro do timeout");
    }

    [Fact]
    public void PicksUpNewTomlWithoutManualReload()
    {
        var engine = BootEngine();
        Assert.Empty(engine.AllProjects());

        WriteProject("demo", "true");
        WaitUntil(() => engine.AllProjects().Count == 1);
        Assert.Equal("demo", engine.GetProject("demo").Id);
    }

    [Fact]
    public void PicksUpEditToStoppedProject()
    {
        var marker = Path.Combine(_configDir, "marker.txt");
        WriteProject("demo", $"echo -n old > {marker}");
        var engine = BootEngine();
        var events = new List<Event>();
        engine.Bus.Subscribe(events.Add);

        engine.Approve("demo");
        engine.Start("demo");
        WaitUntil(() => events.Any(e => e.Type == EventType.Finished));
        Assert.Equal("old", File.ReadAllText(marker));

        WriteProject("demo", $"echo -n new > {marker}");
        WaitUntil(() => engine.GetProject("demo").Start!.Cmd[^1] == $"echo -n new > {marker}");
        // mudar o [start].cmd muda o digest do manifest resolvido -> ManifestRegistry volta pra
        // PendingReview (nunca reaprova sozinho, mesma regra de trust da fase 4) -- reaprovar de
        // propósito aqui é o que prova que o reload pego pelo watcher afetou o manifest de verdade.
        WaitUntil(() => _engine!.Manifest("demo").Status == TrustStatus.PendingReview);
        engine.Approve("demo");

        events.Clear();
        engine.Start("demo");
        WaitUntil(() => events.Any(e => e.Type == EventType.Finished));
        Assert.Equal("new", File.ReadAllText(marker));
    }
}
