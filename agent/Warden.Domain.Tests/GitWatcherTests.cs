using System.Diagnostics;
using Warden.Domain.Git;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_git_watcher.py` — repositórios git reais, nada mockado.</summary>
public sealed class GitWatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void RunGit(string path, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(path);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private (string Up, string Down) ClonePair()
    {
        var remote = Path.Combine(_root, "remote.git");
        Directory.CreateDirectory(remote);
        RunGit(remote, "init", "--bare", "-b", "main");

        var up = Path.Combine(_root, "up");
        Directory.CreateDirectory(up);
        RunGit(up, "init", "-b", "main");
        RunGit(up, "config", "user.email", "up@warden.local");
        RunGit(up, "config", "user.name", "Up");
        File.WriteAllText(Path.Combine(up, "a.txt"), "x");
        RunGit(up, "add", "a.txt");
        RunGit(up, "commit", "-m", "base");
        RunGit(up, "remote", "add", "origin", remote);
        RunGit(up, "push", "-u", "origin", "main");

        var down = Path.Combine(_root, "down");
        RunGit(_root, "clone", remote, down);
        RunGit(down, "config", "user.email", "down@warden.local");
        RunGit(down, "config", "user.name", "Down");

        return (up, down);
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
    public void NotifiesOnTransitionToBehind()
    {
        var (up, down) = ClonePair();
        var calls = new List<int>();
        var watcher = new GitWatcher(down, "origin", 0.2, calls.Add);
        watcher.Start();
        try
        {
            File.WriteAllText(Path.Combine(up, "b.txt"), "novo");
            RunGit(up, "add", "b.txt");
            RunGit(up, "commit", "-m", "novo commit");
            RunGit(up, "push", "origin", "main");

            WaitUntil(() => calls is [1]);
            Thread.Sleep(600); // continua atrás, mas não deve notificar de novo
            Assert.Equal([1], calls);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void NoNotificationWhenUpToDate()
    {
        var (_, down) = ClonePair();
        var calls = new List<int>();
        var watcher = new GitWatcher(down, "origin", 0.2, calls.Add);
        watcher.Start();
        try
        {
            Thread.Sleep(600);
            Assert.Empty(calls);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void NonRepoPathDoesNotCrash()
    {
        var calls = new List<int>();
        var watcher = new GitWatcher(_root, "origin", 0.2, calls.Add);
        watcher.Start();
        try
        {
            Thread.Sleep(500);
            Assert.Empty(calls);
        }
        finally
        {
            watcher.Stop();
        }
    }
}
