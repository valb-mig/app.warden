using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class ManifestDigestTests
{
    private static CommandManifest ManifestWith(params ManifestCommand[] commands) =>
        new() { ProjectId = "p", Commands = commands };

    [Fact]
    public void SameContentProducesSameDigest()
    {
        var a = ManifestWith(new ManifestCommand { Name = "start", Argv = ["python", "main.py"], Cwd = "/tmp/p" });
        var b = ManifestWith(new ManifestCommand { Name = "start", Argv = ["python", "main.py"], Cwd = "/tmp/p" });

        Assert.Equal(ManifestDigest.Compute(a), ManifestDigest.Compute(b));
    }

    [Fact]
    public void DifferentArgvProducesDifferentDigest()
    {
        var a = ManifestWith(new ManifestCommand { Name = "start", Argv = ["python", "main.py"], Cwd = "/tmp/p" });
        var b = ManifestWith(new ManifestCommand { Name = "start", Argv = ["python", "other.py"], Cwd = "/tmp/p" });

        Assert.NotEqual(ManifestDigest.Compute(a), ManifestDigest.Compute(b));
    }

    [Fact]
    public void DifferentCommandOrderProducesDifferentDigest()
    {
        var a = ManifestWith(
            new ManifestCommand { Name = "migrate", Argv = ["true"], Cwd = "/tmp/p" },
            new ManifestCommand { Name = "seed", Argv = ["true"], Cwd = "/tmp/p" });
        var b = ManifestWith(
            new ManifestCommand { Name = "seed", Argv = ["true"], Cwd = "/tmp/p" },
            new ManifestCommand { Name = "migrate", Argv = ["true"], Cwd = "/tmp/p" });

        Assert.NotEqual(ManifestDigest.Compute(a), ManifestDigest.Compute(b));
    }
}
