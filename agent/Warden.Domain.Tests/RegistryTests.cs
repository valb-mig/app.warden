using Xunit;

namespace Warden.Domain.Tests;

public sealed class RegistryTests
{
    private const string ProjectA = """
        id = "a"
        type = "raw"
        path = "/tmp/a"

        [start]
        cmd = ["true"]
        """;

    private const string ProjectB = """
        id = "b"
        type = "raw"
        path = "/tmp/b"

        [start]
        cmd = ["true"]
        """;

    [Fact]
    public void LoadsAllTomlsIgnoringGlobalConfig()
    {
        var root = Directory.CreateTempSubdirectory();
        var projectsDir = Directory.CreateDirectory(Path.Combine(root.FullName, "projects"));
        File.WriteAllText(Path.Combine(projectsDir.FullName, "a.toml"), ProjectA);
        File.WriteAllText(Path.Combine(projectsDir.FullName, "b.toml"), ProjectB);
        File.WriteAllText(Path.Combine(root.FullName, "config.toml"), "api_port = 9000\n");

        var registry = new Registry(root.FullName);
        registry.Load();

        var ids = registry.All().Select(p => p.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "a", "b" }, ids);
        Assert.Equal("/tmp/a", registry.Get("a").Path);
    }

    [Fact]
    public void EmptyWhenDirMissing()
    {
        var root = Directory.CreateTempSubdirectory();
        var registry = new Registry(Path.Combine(root.FullName, "does-not-exist"));

        registry.Load();

        Assert.Empty(registry.All());
    }
}
