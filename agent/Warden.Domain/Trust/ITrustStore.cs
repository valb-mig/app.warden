namespace Warden.Domain.Trust;

public sealed record ApprovedManifest
{
    public required string Digest { get; init; }
    public required string ManifestJson { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
}

/// <summary>
/// Aprovação de pasta é ação exclusiva do Admin, nunca do Console/API remota — este tipo só é
/// implementado (<see cref="SqliteTrustStore"/>) e consumido a partir de código local, nunca
/// exposto por endpoint HTTP (mesma filosofia da decisão #19 do TODO.md pra token escopado).
/// </summary>
public interface ITrustStore
{
    ApprovedManifest? GetApproved(string projectId);

    void Approve(string projectId, string digest, string manifestJson, DateTimeOffset approvedAtUtc);
}
