using System.Diagnostics;
using Warden.Domain.Git;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_git.py` — git real via subprocess, nada mockado.</summary>
public sealed class GitServiceTests
{
    private static void RunGitOrThrow(string path, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(path);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} falhou: {stderr}");
        }
    }

    private static void InitRepo(string path)
    {
        RunGitOrThrow(path, "init", "-b", "main");
        RunGitOrThrow(path, "config", "user.email", "test@warden.local");
        RunGitOrThrow(path, "config", "user.name", "Test");
    }

    private static void Commit(string path, string filename, string message)
    {
        File.WriteAllText(Path.Combine(path, filename), "x");
        RunGitOrThrow(path, "add", filename);
        RunGitOrThrow(path, "commit", "-m", message);
    }

    private static (string Up, string Down) ClonePair(string root)
    {
        var remote = Path.Combine(root, "remote.git");
        Directory.CreateDirectory(remote);
        RunGitOrThrow(remote, "init", "--bare", "-b", "main");

        var up = Path.Combine(root, "up");
        Directory.CreateDirectory(up);
        InitRepo(up);
        Commit(up, "a.txt", "base");
        RunGitOrThrow(up, "remote", "add", "origin", remote);
        RunGitOrThrow(up, "push", "-u", "origin", "main");

        var down = Path.Combine(root, "down");
        RunGitOrThrow(root, "clone", remote, down);
        RunGitOrThrow(down, "config", "user.email", "down@warden.local");
        RunGitOrThrow(down, "config", "user.name", "Down");

        return (up, down);
    }

    [Fact]
    public void NotARepoReturnsNull()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;

        Assert.False(GitService.IsGitRepo(tmpDir));
        Assert.Null(GitService.Info(tmpDir));
    }

    [Fact]
    public void CleanRepoWithCommit()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        InitRepo(tmpDir);
        Commit(tmpDir, "a.txt", "primeiro commit");

        var info = GitService.Info(tmpDir);

        Assert.NotNull(info);
        Assert.Equal("main", info!.Branch);
        Assert.False(info.Dirty);
        Assert.Equal(0, info.DirtyCount);
        Assert.False(info.HasRemote);
        Assert.NotNull(info.LastCommit);
        Assert.Equal("primeiro commit", info.LastCommit!.Subject);
        Assert.Equal("Test", info.LastCommit.Author);
        Assert.NotEmpty(info.LastCommit.Hash);
    }

    [Fact]
    public void DirtyTreeCountsChanges()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        InitRepo(tmpDir);
        Commit(tmpDir, "a.txt", "commit");
        File.WriteAllText(Path.Combine(tmpDir, "b.txt"), "novo"); // untracked
        File.WriteAllText(Path.Combine(tmpDir, "a.txt"), "mudou"); // modified

        var info = GitService.Info(tmpDir);

        Assert.NotNull(info);
        Assert.True(info!.Dirty);
        Assert.Equal(2, info.DirtyCount);
    }

    [Fact]
    public void RepoWithoutCommitsHasNoLastCommit()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        InitRepo(tmpDir);

        var info = GitService.Info(tmpDir);

        Assert.NotNull(info);
        Assert.Null(info!.LastCommit);
        Assert.Null(info.Ahead);
        Assert.Null(info.Behind);
    }

    [Fact]
    public void AheadBehindAgainstUpstream()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var remote = Path.Combine(root, "remote.git");
        Directory.CreateDirectory(remote);
        RunGitOrThrow(remote, "init", "--bare", "-b", "main");

        var work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        InitRepo(work);
        Commit(work, "a.txt", "base");
        RunGitOrThrow(work, "remote", "add", "origin", remote);
        RunGitOrThrow(work, "push", "-u", "origin", "main");

        // 2 commits locais não empurrados → ahead=2, behind=0
        Commit(work, "b.txt", "local 1");
        Commit(work, "c.txt", "local 2");

        var info = GitService.Info(work);

        Assert.NotNull(info);
        Assert.True(info!.HasRemote);
        Assert.Equal(2, info.Ahead);
        Assert.Equal(0, info.Behind);
    }

    [Fact]
    public void CommandUnknownVerbThrows()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;
        InitRepo(tmpDir);

        Assert.Throws<GitVerbNotSupportedException>(() => GitService.Command(tmpDir, "clone"));
    }

    [Fact]
    public void CommandOnNonRepoIsRefused()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;

        var result = GitService.Command(tmpDir, "fetch");

        Assert.True(result.Refused);
        Assert.False(result.Ok);
    }

    [Fact]
    public void PullRefusedWhenDirty()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var (_, down) = ClonePair(root);
        File.WriteAllText(Path.Combine(down, "a.txt"), "mexido local"); // suja o tree

        var result = GitService.Command(down, "pull");

        Assert.True(result.Refused);
        Assert.Contains("sujo", result.Output);
    }

    [Fact]
    public void SyncFastForwardsWhenBehindAndClean()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var (up, down) = ClonePair(root);
        // upstream avança e empurra → down fica behind
        Commit(up, "b.txt", "novo no origin");
        RunGitOrThrow(up, "push", "origin", "main");

        var result = GitService.Command(down, "sync");

        Assert.True(result.Ok);
        Assert.False(result.Refused);
        // down agora tem o commit novo
        var info = GitService.Info(down);
        Assert.NotNull(info);
        Assert.Equal(0, info!.Behind);
        Assert.True(File.Exists(Path.Combine(down, "b.txt")));
    }

    [Fact]
    public void SyncNoopWhenUpToDate()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var (_, down) = ClonePair(root);

        var result = GitService.Command(down, "sync");

        Assert.True(result.Ok);
        Assert.Equal("já atualizado", result.Output);
    }

    [Fact]
    public void FetchUpdatesRefsWithoutTouchingTree()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var (up, down) = ClonePair(root);
        Commit(up, "b.txt", "novo no origin");
        RunGitOrThrow(up, "push", "origin", "main");

        var result = GitService.Command(down, "fetch");

        Assert.True(result.Ok);
        // fetch atualiza behind mas NÃO traz o arquivo pro working tree
        var info = GitService.Info(down);
        Assert.NotNull(info);
        Assert.Equal(1, info!.Behind);
        Assert.False(File.Exists(Path.Combine(down, "b.txt")));
    }
}
