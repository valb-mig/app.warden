using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class CommandExecutorTests
{
    private static ManifestCommand Command(IReadOnlyList<string> argv, string? cwd = null) => new()
    {
        Name = "test",
        Argv = argv,
        Cwd = cwd ?? Directory.GetCurrentDirectory(),
    };

    [Fact]
    public void CapturesStdoutAndExitCodeZero()
    {
        var result = CommandExecutor.Run(Command(["python3", "-c", "print('hello')"]));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void CapturesNonZeroExitCode()
    {
        var result = CommandExecutor.Run(Command(["python3", "-c", "import sys; sys.exit(3)"]));

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void CapturesStderrToo()
    {
        var result = CommandExecutor.Run(
            Command(["python3", "-c", "import sys; print('oops', file=sys.stderr)"]));

        Assert.Contains("oops", result.Output);
    }

    [Fact]
    public void RunsInGivenWorkingDirectory()
    {
        var tmpDir = Directory.CreateTempSubdirectory().FullName;

        var result = CommandExecutor.Run(Command(["python3", "-c", "import os; print(os.getcwd())"], tmpDir));

        Assert.Contains(tmpDir, result.Output);
    }
}
