namespace Warden.Contracts.Admin;

public sealed record GlobalConfigDto
{
    public required int ApiPort { get; init; }
    public required string NotifyChannel { get; init; }
    public string? NtfyTopic { get; init; }
    public required string NtfyServer { get; init; }
    public required IReadOnlyList<string> ScanPaths { get; init; }
}
