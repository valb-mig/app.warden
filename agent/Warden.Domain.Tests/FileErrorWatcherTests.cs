using Warden.Domain.Watch;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_file_error_watcher.py`.</summary>
public sealed class FileErrorWatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly string _logFile;

    public FileErrorWatcherTests()
    {
        _logFile = Path.Combine(_root, "app.log");
        File.WriteAllText(_logFile, "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static FileErrorWatcher MakeWatcher(string path, IEnumerable<string> patterns, List<string> calls) =>
        new(path, patterns, calls.Add);

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
    public void SingleLineMatchFlushesByIdle()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, ["ERROR"], calls);
        watcher.Start();
        try
        {
            File.AppendAllText(_logFile, "[2024-01-01 00:00:00] production.ERROR: something broke\n");
            WaitUntil(() => calls.Count == 1);
            Assert.Contains("something broke", calls[0]);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void NoMatchNeverCallsBack()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, ["ERROR"], calls);
        watcher.Start();
        try
        {
            File.AppendAllText(_logFile, "[2024-01-01 00:00:00] production.INFO: all good\n");
            Thread.Sleep(3000);
            Assert.Empty(calls);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void MultilineStacktraceGroupedAsOneEntry()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, [@"\bException\b"], calls);
        watcher.Start();
        try
        {
            File.AppendAllText(_logFile,
                "[2024-01-01 00:00:00] production.ERROR: boom\n" +
                "Stack trace:\n" +
                "#0 Exception thrown here\n");
            WaitUntil(() => calls.Count == 1);
            Assert.Contains("Exception", calls[0]);
            Assert.Equal(2, calls[0].Count(c => c == '\n')); // 3 linhas viraram 1 entrada só
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void NewEntryStartFlushesPreviousPending()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, ["ERROR"], calls);
        watcher.Start();
        try
        {
            File.AppendAllText(_logFile,
                "[2024-01-01 00:00:00] production.ERROR: first\n" +
                "continuation of first\n");
            Thread.Sleep(500); // bem menor que o idle threshold (2s) — ainda não deve ter flushado
            Assert.Empty(calls);

            File.AppendAllText(_logFile, "[2024-01-01 00:00:01] production.INFO: second, sem match\n");
            WaitUntil(() => calls.Count == 1);
            Assert.Contains("first", calls[0]);
            Assert.Contains("continuation of first", calls[0]);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void RotationByNewFileIsDetected()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, ["ERROR"], calls);
        watcher.Start();
        try
        {
            Thread.Sleep(1200); // garante baseline (identidade/pos) capturado antes de rotacionar
            File.Delete(_logFile);
            File.WriteAllText(_logFile, "[2024-01-01 00:00:00] production.ERROR: after rotation\n");
            WaitUntil(() => calls.Count == 1);
            Assert.Contains("after rotation", calls[0]);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void TruncationSameIdentityCopytruncate()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(_logFile, ["ERROR"], calls);
        watcher.Start();
        try
        {
            Thread.Sleep(1200); // baseline capturado
            File.AppendAllText(_logFile, string.Concat(Enumerable.Repeat("padding line here\n", 20)));
            Thread.Sleep(1200); // padding processado, pos avançou
            File.WriteAllText(_logFile, "[2024-01-01 00:00:00] production.ERROR: after truncate\n");
            WaitUntil(() => calls.Count == 1);
            Assert.Contains("after truncate", calls[0]);
        }
        finally
        {
            watcher.Stop();
        }
    }

    [Fact]
    public void MissingFileDoesNotCrash()
    {
        var calls = new List<string>();
        var watcher = MakeWatcher(Path.Combine(_root, "does-not-exist.log"), ["ERROR"], calls);
        watcher.Start();
        try
        {
            Thread.Sleep(1500);
            Assert.Empty(calls);
        }
        finally
        {
            watcher.Stop();
        }
    }
}
