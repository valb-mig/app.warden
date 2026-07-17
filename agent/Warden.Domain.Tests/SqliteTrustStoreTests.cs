using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class SqliteTrustStoreTests
{
    private static SqliteTrustStore NewStore() =>
        new(Path.Combine(Directory.CreateTempSubdirectory().FullName, "warden.db"));

    [Fact]
    public void GetApprovedReturnsNullWhenNeverApproved()
    {
        var store = NewStore();

        Assert.Null(store.GetApproved("unknown"));
    }

    [Fact]
    public void ApproveThenGetApprovedRoundTrips()
    {
        var store = NewStore();
        var approvedAt = DateTimeOffset.Parse("2026-07-14T12:00:00Z");

        store.Approve("p", "digest-1", "[{\"Name\":\"start\"}]", approvedAt);
        var approved = store.GetApproved("p");

        Assert.NotNull(approved);
        Assert.Equal("digest-1", approved!.Digest);
        Assert.Equal("[{\"Name\":\"start\"}]", approved.ManifestJson);
        Assert.Equal(approvedAt, approved.ApprovedAtUtc);
    }

    [Fact]
    public void ApprovingAgainOverwritesPreviousApproval()
    {
        var store = NewStore();
        store.Approve("p", "digest-1", "[]", DateTimeOffset.Parse("2026-07-14T12:00:00Z"));

        store.Approve("p", "digest-2", "[{\"Name\":\"migrate\"}]", DateTimeOffset.Parse("2026-07-14T13:00:00Z"));
        var approved = store.GetApproved("p");

        Assert.Equal("digest-2", approved!.Digest);
    }

    [Fact]
    public void PersistsAcrossNewStoreInstancesOnSameFile()
    {
        var dbPath = Path.Combine(Directory.CreateTempSubdirectory().FullName, "warden.db");
        new SqliteTrustStore(dbPath).Approve("p", "digest-1", "[]", DateTimeOffset.UtcNow);

        var reopened = new SqliteTrustStore(dbPath);

        Assert.NotNull(reopened.GetApproved("p"));
    }
}
