using Warden.Domain.Adapters;
using Warden.Domain.Config;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>
/// Spawna processos `python3` de verdade (mesma escolha do teste equivalente em `engine/tests/`) —
/// garante paridade de comportamento com o motor Python que este código está substituindo.
/// </summary>
public sealed class ProcessAdapterTests
{
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

    private static ProjectConfig Project(string tmpPath, IReadOnlyList<string> cmd, bool captureStdout = true) =>
        new()
        {
            Id = "fake",
            Path = tmpPath,
            Type = "raw",
            Start = new StartConfig { Cmd = [.. cmd], CaptureStdout = captureStdout },
        };

    [Fact]
    public void StartCapturesStdoutAndReportsRunning()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        // sem -u nem flush manual: o adapter usa PTY, então o próprio python já faz flush por
        // linha por enxergar um terminal — precisa chegar sem isso.
        const string script = "import time; print('hello'); time.sleep(2)";
        var adapter = new RawAdapter(Project(tmpDir, ["python3", "-c", script]));

        adapter.Start();
        try
        {
            WaitUntil(() => adapter.Logs().Any(line => line.Contains("hello")));
            var status = adapter.Status();
            Assert.True(status.Running);
            Assert.NotNull(status.Pid);
        }
        finally
        {
            adapter.Stop();
        }

        WaitUntil(() => !adapter.Status().Running);
    }

    [Fact]
    public void StopTerminatesProcess()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        var adapter = new RawAdapter(Project(tmpDir, ["python3", "-c", "import time; time.sleep(30)"]));

        adapter.Start();
        WaitUntil(() => adapter.Status().Running);

        adapter.Stop();

        WaitUntil(() => !adapter.Status().Running, timeoutSeconds: 12.0);
    }

    [Fact]
    public void StatusBeforeStartIsNotRunning()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        var adapter = new RawAdapter(Project(tmpDir, ["python3", "-c", "pass"]));

        Assert.False(adapter.Status().Running);
    }
}
