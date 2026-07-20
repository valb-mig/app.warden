namespace Warden.Domain.Discovery;

public sealed record DiscoveredProject(string Name, string Path, string Type);

public sealed record BrowseEntry(string Name, string Path);

public sealed record BrowseResult(string Path, string? Parent, IReadOnlyList<BrowseEntry> Entries);
