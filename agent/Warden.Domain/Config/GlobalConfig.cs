namespace Warden.Domain.Config;

public sealed class GlobalConfig
{
    public int ApiPort { get; init; } = 8420;
    public string NotifyChannel { get; init; } = "none";
    public string? NtfyTopic { get; init; }
    public string NtfyServer { get; init; } = "https://ntfy.sh";
    public List<string> ScanPaths { get; init; } = [];

    public override bool Equals(object? obj) =>
        obj is GlobalConfig other
        && ApiPort == other.ApiPort
        && NotifyChannel == other.NotifyChannel
        && NtfyTopic == other.NtfyTopic
        && NtfyServer == other.NtfyServer
        && ScanPaths.SequenceEqual(other.ScanPaths);

    public override int GetHashCode() =>
        HashCode.Combine(ApiPort, NotifyChannel, NtfyTopic, NtfyServer, ScanPaths.Count);
}
