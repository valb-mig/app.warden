namespace Warden.Domain.Trust;

/// <summary>
/// Snapshot imutável servido tanto pra listar botões quanto pra executá-los — garante que as duas
/// operações sempre veem exatamente o mesmo conteúdo (ver NEW_CONTEXT.md §10.4). Só é substituído
/// atomicamente por <see cref="ManifestRegistry"/>, nunca mutado in-place.
/// </summary>
public sealed record ProjectManifestSnapshot
{
    public required CommandManifest Manifest { get; init; }
    public required string Digest { get; init; }
    public required TrustStatus Status { get; init; }

    /// <summary>JSON do último manifesto aprovado (pra diff numa UI de Admin futura), null se nunca aprovado.</summary>
    public string? ApprovedManifestJson { get; init; }
}
