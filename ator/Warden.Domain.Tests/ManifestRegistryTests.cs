using Warden.Domain.Config;
using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class ManifestRegistryTests
{
    private static ProjectConfig Project(params ActionConfig[] actions) => new()
    {
        Id = "p",
        Path = "/tmp/p",
        Type = "raw",
        Start = new StartConfig { Cmd = ["true"] },
        Actions = [.. actions],
    };

    private static (ManifestRegistry Registry, SqliteTrustStore Store) NewRegistry()
    {
        var store = new SqliteTrustStore(Path.Combine(Directory.CreateTempSubdirectory().FullName, "warden.db"));
        return (new ManifestRegistry(store), store);
    }

    [Fact]
    public void RefreshOnNeverApprovedProjectIsNeverApproved()
    {
        var (registry, _) = NewRegistry();

        var snapshot = registry.Refresh(Project());

        Assert.Equal(TrustStatus.NeverApproved, snapshot.Status);
    }

    [Fact]
    public void ApproveFlipsStatusToApproved()
    {
        var (registry, _) = NewRegistry();
        var config = Project();
        registry.Refresh(config);

        var approved = registry.Approve(config);

        Assert.Equal(TrustStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedManifestJson);
    }

    [Fact]
    public void RefreshAfterConfigChangeIsPendingReviewNotSilentlyApproved()
    {
        var (registry, _) = NewRegistry();
        var original = Project();
        registry.Approve(original);

        var changed = Project(new ActionConfig { Name = "migrate", Cmd = ["true"] });
        var refreshed = registry.Refresh(changed);

        Assert.Equal(TrustStatus.PendingReview, refreshed.Status);
    }

    [Fact]
    public void ApproveAfterChangeApprovesTheNewContentNotTheOld()
    {
        var (registry, _) = NewRegistry();
        var original = Project();
        registry.Approve(original);

        var changed = Project(new ActionConfig { Name = "migrate", Cmd = ["true"] });
        var approved = registry.Approve(changed);

        Assert.Equal(TrustStatus.Approved, approved.Status);
        Assert.Equal(2, approved.Manifest.Commands.Count); // start + migrate
    }

    [Fact]
    public void GetReturnsSameSnapshotReferenceUntilNextRefresh()
    {
        var (registry, _) = NewRegistry();
        var config = Project();
        registry.Refresh(config);

        var first = registry.Get(config.Id);
        var second = registry.Get(config.Id);

        Assert.Same(first, second); // mesma referência: listar e executar nunca divergem entre si
    }

    [Fact]
    public void GetReturnsNullForUnknownProject()
    {
        var (registry, _) = NewRegistry();

        Assert.Null(registry.Get("never-refreshed"));
    }
}
