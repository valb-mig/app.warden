namespace Warden.Contracts.Admin;

/// <summary>
/// DTO da superfície de Admin (IPC local, nunca pela rede — ver NEW_CONTEXT.md §10.7). Primeiro uso
/// real de Warden.Contracts: schemas compartilhados entre Warden.Agent (produz) e Warden.Admin
/// (consome), sem que o Admin precise referenciar Warden.Domain diretamente.
/// </summary>
public sealed record AdminProjectDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }

    /// <summary>Espelha `Warden.Domain.Trust.TrustStatus` como string (NeverApproved/PendingReview/Approved).</summary>
    public required string Status { get; init; }
    public required IReadOnlyList<string> Commands { get; init; }

    /// <summary>
    /// Último manifesto aprovado (nomes de comando), null se nunca aprovado. Junto com <see cref="Commands"/>
    /// (o manifesto atual) dá pro Admin montar o diff "aprovado vs. atual" antes do clique de reaprovar
    /// (NEW_CONTEXT.md §10.3) — nunca reaprovação cega.
    /// </summary>
    public IReadOnlyList<string>? ApprovedCommands { get; init; }
}
