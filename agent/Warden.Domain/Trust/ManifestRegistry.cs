using System.Collections.Concurrent;
using System.Text.Json;
using Warden.Domain.Config;

namespace Warden.Domain.Trust;

/// <summary>
/// Guarda, por projeto, o <see cref="ProjectManifestSnapshot"/> vivo em memória. `Refresh` é chamado
/// pelo mesmo poll de hot-reload que já recarrega `.toml` (ver TODO.md decisão #19/`ProjectsWatcher`)
/// — recalcula o manifesto e o status de confiança, e troca o snapshot inteiro atomicamente (a
/// escrita no <see cref="ConcurrentDictionary{TKey,TValue}"/> é a única operação, nunca mutação
/// parcial de um snapshot existente).
/// </summary>
public sealed class ManifestRegistry(ITrustStore trustStore)
{
    private readonly ConcurrentDictionary<string, ProjectManifestSnapshot> _snapshots = new();

    public ProjectManifestSnapshot? Get(string projectId) => _snapshots.GetValueOrDefault(projectId);

    public ProjectManifestSnapshot Refresh(ProjectConfig config)
    {
        var manifest = ManifestBuilder.Build(config);
        var digest = ManifestDigest.Compute(manifest);
        var approved = trustStore.GetApproved(config.Id);

        var status = approved switch
        {
            null => TrustStatus.NeverApproved,
            { } a when a.Digest == digest => TrustStatus.Approved,
            _ => TrustStatus.PendingReview,
        };

        var snapshot = new ProjectManifestSnapshot
        {
            Manifest = manifest,
            Digest = digest,
            Status = status,
            ApprovedManifestJson = approved?.ManifestJson,
        };

        _snapshots[config.Id] = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Aprova exatamente o que está resolvido *agora* — recalcula antes de gravar (evita aprovar um
    /// snapshot desatualizado por uma corrida entre a última leitura do disco e o clique de aprovar).
    /// </summary>
    public ProjectManifestSnapshot Approve(ProjectConfig config)
    {
        var current = Refresh(config);
        var manifestJson = JsonSerializer.Serialize(current.Manifest.Commands);
        trustStore.Approve(config.Id, current.Digest, manifestJson, DateTimeOffset.UtcNow);

        var approvedSnapshot = current with { Status = TrustStatus.Approved, ApprovedManifestJson = manifestJson };
        _snapshots[config.Id] = approvedSnapshot;
        return approvedSnapshot;
    }
}
