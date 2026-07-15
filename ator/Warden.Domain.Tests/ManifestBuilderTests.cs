using Warden.Domain.Config;
using Warden.Domain.Trust;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class ManifestBuilderTests
{
    private static ProjectConfig DockerProject() => new()
    {
        Id = "leadmaster",
        Path = "/tmp/leadmaster",
        Type = "docker",
        Actions =
        [
            new ActionConfig { Name = "migrate", Cmd = ["docker", "compose", "exec", "app", "php", "artisan", "migrate", "--force"], Destructive = true },
            new ActionConfig { Name = "seed", Cmd = ["docker", "compose", "exec", "app", "php", "artisan", "db:seed"], Destructive = true },
        ],
    };

    private static ProjectConfig ProcessProject() => new()
    {
        Id = "caffeshop-bot",
        Path = "/tmp/caffeshop",
        Type = "python",
        Start = new StartConfig { Cmd = ["python", "main.py"], CaptureStdout = true },
    };

    [Fact]
    public void IncludesStartAsFirstCommand()
    {
        var manifest = ManifestBuilder.Build(ProcessProject());

        Assert.Single(manifest.Commands);
        Assert.Equal("start", manifest.Commands[0].Name);
        Assert.Equal(["python", "main.py"], manifest.Commands[0].Argv);
        Assert.Equal("/tmp/caffeshop", manifest.Commands[0].Cwd);
    }

    [Fact]
    public void IncludesActionsAfterStartPreservingOrderAndFlags()
    {
        var manifest = ManifestBuilder.Build(DockerProject());

        Assert.Equal(["migrate", "seed"], manifest.Commands.Select(c => c.Name));
        Assert.True(manifest.Commands[0].Destructive);
    }

    [Fact]
    public void BuildIsDeterministic()
    {
        var config = DockerProject();

        var first = ManifestBuilder.Build(config);
        var second = ManifestBuilder.Build(config);

        Assert.Equal(ManifestDigest.Compute(first), ManifestDigest.Compute(second));
    }
}
