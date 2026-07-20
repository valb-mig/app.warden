using Warden.Domain.Config;
using Warden.Domain.Discovery;
using Xunit;

namespace Warden.Domain.Tests;

/// <summary>Mirror de `engine/tests/test_discovery.py` — real filesystem, sem mock.</summary>
public sealed class ProjectDiscoveryTests : IDisposable
{
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "warden-discovery-tests-" + Guid.NewGuid().ToString("N"));

    public ProjectDiscoveryTests() => Directory.CreateDirectory(_configDir);

    public void Dispose() => Directory.Delete(_configDir, recursive: true);

    [Fact]
    public void AddScanPathPersistsAndDedupes()
    {
        var scanRoot = Path.Combine(_configDir, "myprojects");
        Directory.CreateDirectory(scanRoot);

        var config = ProjectDiscovery.AddScanPath(_configDir, scanRoot);
        Assert.Equal([scanRoot], config.ScanPaths);

        config = ProjectDiscovery.AddScanPath(_configDir, scanRoot);
        Assert.Equal([scanRoot], config.ScanPaths);

        var reloaded = ConfigLoader.LoadGlobalConfig(Path.Combine(_configDir, "config.toml"));
        Assert.Equal([scanRoot], reloaded.ScanPaths);
    }

    [Fact]
    public void AddScanPathRejectsNonDirectory() =>
        Assert.Throws<DirectoryNotFoundException>(() => ProjectDiscovery.AddScanPath(_configDir, Path.Combine(_configDir, "nope")));

    [Fact]
    public void RemoveScanPath()
    {
        var scanRoot = Path.Combine(_configDir, "myprojects");
        Directory.CreateDirectory(scanRoot);
        ProjectDiscovery.AddScanPath(_configDir, scanRoot);

        var config = ProjectDiscovery.RemoveScanPath(_configDir, scanRoot);
        Assert.Empty(config.ScanPaths);
    }

    [Fact]
    public void DiscoverProjectsListsNewAndSkipsRegisteredAndHidden()
    {
        var scanRoot = Path.Combine(_configDir, "myprojects");
        Directory.CreateDirectory(scanRoot);
        var alreadyRegistered = Path.Combine(scanRoot, "already-registered");
        Directory.CreateDirectory(alreadyRegistered);
        var newPython = Path.Combine(scanRoot, "new-python");
        Directory.CreateDirectory(newPython);
        File.WriteAllText(Path.Combine(newPython, "requirements.txt"), "");
        Directory.CreateDirectory(Path.Combine(scanRoot, ".hidden"));

        var projectsDir = Path.Combine(_configDir, "projects");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "already.toml"),
            $"id = \"already\"\ntype = \"raw\"\npath = \"{alreadyRegistered.Replace("\\", "\\\\")}\"\n");
        var registry = new Registry(_configDir);
        registry.Load();

        var config = ProjectDiscovery.AddScanPath(_configDir, scanRoot);
        var discovered = ProjectDiscovery.DiscoverProjects(config, registry);

        Assert.Equal(["new-python"], discovered.Select(d => d.Name));
        Assert.Equal("python", discovered[0].Type);
    }

    [Fact]
    public void DiscoverProjectsIgnoresMissingScanPath()
    {
        var config = ProjectDiscovery.AddScanPath(_configDir, _configDir);
        config = new GlobalConfig
        {
            ApiPort = config.ApiPort,
            NotifyChannel = config.NotifyChannel,
            NtfyTopic = config.NtfyTopic,
            NtfyServer = config.NtfyServer,
            ScanPaths = [.. config.ScanPaths, Path.Combine(_configDir, "gone")],
        };

        var registry = new Registry(_configDir);
        registry.Load();

        var discovered = ProjectDiscovery.DiscoverProjects(config, registry);
        Assert.Empty(discovered);
    }

    [Fact]
    public void BrowseDefaultsToHome()
    {
        var result = ProjectDiscovery.BrowseDirectory(null);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), result.Path);
    }

    [Fact]
    public void BrowseListsSubdirectoriesAndParent()
    {
        var subA = Path.Combine(_configDir, "sub-a");
        var subB = Path.Combine(_configDir, "sub-b");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);
        Directory.CreateDirectory(Path.Combine(_configDir, ".hidden"));
        File.WriteAllText(Path.Combine(_configDir, "a-file.txt"), "");

        var result = ProjectDiscovery.BrowseDirectory(_configDir);

        Assert.Equal(Path.GetFullPath(_configDir), result.Path);
        Assert.Equal(Directory.GetParent(_configDir)!.FullName, result.Parent);
        Assert.Equal(["sub-a", "sub-b"], result.Entries.Select(e => e.Name).OrderBy(n => n));
    }

    [Fact]
    public void BrowseRootHasNoParent()
    {
        var result = ProjectDiscovery.BrowseDirectory("/");
        Assert.Null(result.Parent);
    }

    [Fact]
    public void BrowseRejectsNonDirectory() =>
        Assert.Throws<DirectoryNotFoundException>(() => ProjectDiscovery.BrowseDirectory(Path.Combine(_configDir, "nope")));
}
