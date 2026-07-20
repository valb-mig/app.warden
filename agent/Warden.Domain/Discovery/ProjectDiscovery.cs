using Warden.Domain.Config;

namespace Warden.Domain.Discovery;

/// <summary>Mirror 1:1 de `discovery.py` — varre `scan_paths` por subpastas ainda não registradas.</summary>
public static class ProjectDiscovery
{
    public static BrowseResult BrowseDirectory(string? rawPath)
    {
        var target = rawPath is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(Scaffold.ExpandHome(rawPath));
        if (!Directory.Exists(target))
        {
            throw new DirectoryNotFoundException($"path não é diretório: {target}");
        }

        var entries = new List<BrowseEntry>();
        try
        {
            foreach (var entry in Directory.GetDirectories(target).OrderBy(e => e, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;
                entries.Add(new BrowseEntry(name, entry));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // sem permissão de listar — mesmo comportamento do `except PermissionError: pass` do Python
        }

        var parent = Directory.GetParent(target)?.FullName;
        return new BrowseResult(target, parent, entries);
    }

    public static GlobalConfig AddScanPath(string configDir, string rawPath)
    {
        var resolved = Path.GetFullPath(Scaffold.ExpandHome(rawPath));
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"path não é diretório: {resolved}");
        }

        var configPath = Path.Combine(configDir, "config.toml");
        var config = ConfigLoader.LoadGlobalConfig(configPath);
        if (config.ScanPaths.Contains(resolved)) return config;

        var updated = WithScanPaths(config, [.. config.ScanPaths, resolved]);
        ConfigLoader.SaveGlobalConfig(configPath, updated);
        return updated;
    }

    public static GlobalConfig RemoveScanPath(string configDir, string rawPath)
    {
        var resolved = Path.GetFullPath(Scaffold.ExpandHome(rawPath));
        var configPath = Path.Combine(configDir, "config.toml");
        var config = ConfigLoader.LoadGlobalConfig(configPath);

        var updated = WithScanPaths(config, config.ScanPaths.Where(p => p != resolved).ToList());
        ConfigLoader.SaveGlobalConfig(configPath, updated);
        return updated;
    }

    public static IReadOnlyList<DiscoveredProject> DiscoverProjects(GlobalConfig config, Registry registry)
    {
        var registered = registry.All()
            .Select(p => Path.GetFullPath(Scaffold.ExpandHome(p.Path)))
            .ToHashSet();
        var discovered = new List<DiscoveredProject>();
        var seen = new HashSet<string>();

        foreach (var rawScanPath in config.ScanPaths)
        {
            var scanPath = Scaffold.ExpandHome(rawScanPath);
            if (!Directory.Exists(scanPath)) continue;

            foreach (var entry in Directory.GetDirectories(scanPath).OrderBy(e => e, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;
                var resolved = Path.GetFullPath(entry);
                if (registered.Contains(resolved) || seen.Contains(resolved)) continue;
                seen.Add(resolved);

                discovered.Add(new DiscoveredProject(name, resolved, Scaffold.DetectType(resolved)));
            }
        }
        return discovered;
    }

    private static GlobalConfig WithScanPaths(GlobalConfig config, List<string> scanPaths) => new()
    {
        ApiPort = config.ApiPort,
        NotifyChannel = config.NotifyChannel,
        NtfyTopic = config.NtfyTopic,
        NtfyServer = config.NtfyServer,
        ScanPaths = scanPaths,
    };
}
