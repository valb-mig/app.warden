using System.Diagnostics;
using Warden.Agent.Hubs;
using Xunit;

namespace Warden.Agent.Tests;

public sealed class ChildProcessRegistryTests
{
    private static Process SpawnLongRunning()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import time; time.sleep(60)");
        return Process.Start(psi)!;
    }

    [Fact]
    public void KillAllTerminatesRegisteredProcesses()
    {
        var registry = new ChildProcessRegistry();
        using var process = SpawnLongRunning();
        registry.Register("conn-1", process);

        registry.KillAll("conn-1");

        process.WaitForExit(TimeSpan.FromSeconds(5));
        Assert.True(process.HasExited);
    }

    [Fact]
    public void KillAllForUnknownConnectionIsNoop()
    {
        var registry = new ChildProcessRegistry();

        registry.KillAll("never-registered");
    }

    [Fact]
    public void KillAllRemovesConnectionSoSecondCallIsNoop()
    {
        var registry = new ChildProcessRegistry();
        using var process = SpawnLongRunning();
        registry.Register("conn-1", process);

        registry.KillAll("conn-1");
        registry.KillAll("conn-1"); // não deve lançar nem tentar matar de novo

        process.WaitForExit(TimeSpan.FromSeconds(5));
        Assert.True(process.HasExited);
    }
}
