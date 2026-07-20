using Warden.Domain.Events;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_store.py` — SQLite real em disco, nada mockado.</summary>
public sealed class SqliteEventStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RecordAndHistoryOrder()
    {
        var store = new SqliteEventStore(Path.Combine(_dir, "warden.db"));

        store.Record(new Event("demo", EventType.Started));
        store.Record(new Event("demo", EventType.Error, "exit=1"));
        store.Record(new Event("other", EventType.Started));

        var history = store.History("demo");

        Assert.Equal(2, history.Count);
        Assert.Equal("error", history[0].Type); // mais recente primeiro
        Assert.Equal("exit=1", history[0].Message);
        Assert.Equal("started", history[1].Type);
    }

    [Fact]
    public void HistoryRespectsLimit()
    {
        var store = new SqliteEventStore(Path.Combine(_dir, "warden.db"));
        for (var i = 0; i < 5; i++) store.Record(new Event("demo", EventType.Started));

        Assert.Equal(2, store.History("demo", limit: 2).Count);
    }

    [Fact]
    public void RecordActionAndAuditRoundTrip()
    {
        var store = new SqliteEventStore(Path.Combine(_dir, "warden.db"));

        store.RecordAction("demo", "wipe", ["echo", "wiped"], confirmed: true, exitCode: 0);
        var audit = store.ActionAudit("demo");

        Assert.Single(audit);
        Assert.Equal("wipe", audit[0].ActionName);
        Assert.True(audit[0].Confirmed);
        Assert.Equal(["echo", "wiped"], audit[0].Cmd);
    }
}
