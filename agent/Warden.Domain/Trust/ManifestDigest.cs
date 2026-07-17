using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Warden.Domain.Trust;

/// <summary>
/// Hash da superfície executável **já resolvida** (nome, argv, cwd), não do arquivo `.toml` cru —
/// mudança cosmética (comentário, espaço) no config não deveria forçar reaprovação, só mudança que
/// altera o que um botão de fato roda (ver NEW_CONTEXT.md §10.3). JSON sem indentação e ordem de
/// propriedade fixa (declaração do record) garantem que o mesmo conteúdo sempre gera o mesmo hash.
/// </summary>
public static class ManifestDigest
{
    public static string Compute(CommandManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest.Commands);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(hash);
    }
}
