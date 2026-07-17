using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Cobre o `Engine` (facade Registry+AdapterFactory+ManifestRegistry) contra um projeto real em
/// disco, mesmo rigor de "sem mock" das fases anteriores — spawn real de `python3`/`echo`.
/// </summary>
public sealed class EngineTests : IDisposable
{
    private const string ProjectTomlTemplate = """
        id = "p"
        type = "raw"
        path = "{PROJECT_PATH}"

        [start]
        cmd = ["python3", "-c", "import time; print('hello'); time.sleep(5)"]
        capture_stdout = true

        [[actions]]
        name = "greet"
        cmd = ["echo", "hi"]

        [[actions]]
        name = "wipe"
        cmd = ["true"]
        destructive = true

        [[actions]]
        name = "shell"
        cmd = ["true"]
        interactive = true
        """;

    private readonly string _configDir;
    private readonly Engine _engine;

    public EngineTests()
    {
        _configDir = Directory.CreateTempSubdirectory().FullName;
        var projectPath = Directory.CreateTempSubdirectory().FullName;
        var projectsDir = Directory.CreateDirectory(Path.Combine(_configDir, "projects"));
        var toml = ProjectTomlTemplate.Replace("{PROJECT_PATH}", projectPath);
        File.WriteAllText(Path.Combine(projectsDir.FullName, "p.toml"), toml);

        var registry = new Registry(_configDir);
        var trustStore = new SqliteTrustStore(Path.Combine(_configDir, "warden.db"));
        var manifestRegistry = new ManifestRegistry(trustStore);
        _engine = new Engine(registry, manifestRegistry);
        _engine.Boot();
    }

    public void Dispose()
    {
        try
        {
            _engine.Stop("p");
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void GetProjectThrowsForUnknownId()
    {
        Assert.Throws<KeyNotFoundException>(() => _engine.GetProject("nope"));
    }

    [Fact]
    public void FreshProjectIsNeverApproved()
    {
        Assert.Equal(TrustStatus.NeverApproved, _engine.Manifest("p").Status);
    }

    [Fact]
    public void StartRefusesWhenNotApproved()
    {
        Assert.Throws<ManifestNotApprovedException>(() => _engine.Start("p"));
    }

    [Fact]
    public void RunActionRefusesWhenNotApproved()
    {
        Assert.Throws<ManifestNotApprovedException>(() => _engine.RunAction("p", "greet", confirmed: false));
    }

    [Fact]
    public void StartSucceedsAfterApproval()
    {
        _engine.Approve("p");

        _engine.Start("p");
        try
        {
            WaitUntil(() => _engine.Status("p").Running);
            Assert.True(_engine.Status("p").Running);
        }
        finally
        {
            _engine.Stop("p");
        }
    }

    [Fact]
    public void RunActionExecutesAndCapturesOutput()
    {
        _engine.Approve("p");

        var result = _engine.RunAction("p", "greet", confirmed: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hi", result.Output);
    }

    [Fact]
    public void RunActionUnknownNameThrows()
    {
        _engine.Approve("p");

        Assert.Throws<KeyNotFoundException>(() => _engine.RunAction("p", "does-not-exist", confirmed: false));
    }

    [Fact]
    public void RunActionCannotTargetStart()
    {
        _engine.Approve("p");

        Assert.Throws<KeyNotFoundException>(() => _engine.RunAction("p", "start", confirmed: false));
    }

    [Fact]
    public void RunActionDestructiveWithoutConfirmThrows()
    {
        _engine.Approve("p");

        Assert.Throws<ConfirmationRequiredException>(() => _engine.RunAction("p", "wipe", confirmed: false));
    }

    [Fact]
    public void RunActionDestructiveWithConfirmSucceeds()
    {
        _engine.Approve("p");

        var result = _engine.RunAction("p", "wipe", confirmed: true);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void RunActionInteractiveThrows()
    {
        _engine.Approve("p");

        Assert.Throws<ActionInteractiveException>(() => _engine.RunAction("p", "shell", confirmed: false));
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
}
