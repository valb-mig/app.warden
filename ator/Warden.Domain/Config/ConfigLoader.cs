using System.Text.Json;
using Tomlyn;

namespace Warden.Domain.Config;

public static class ConfigLoader
{
    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static ProjectConfig LoadProjectConfig(string path)
    {
        var text = File.ReadAllText(path);
        var config = TomlSerializer.Deserialize<ProjectConfig>(text, Options)
            ?? throw new InvalidDataException($"config vazio: {path}");

        if (!ProjectConfig.KnownTypes.Contains(config.Type))
        {
            throw new InvalidDataException(
                $"projeto \"{config.Id}\" ({path}): type \"{config.Type}\" desconhecido");
        }

        return config;
    }

    public static GlobalConfig LoadGlobalConfig(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new GlobalConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, RenderGlobalConfigToml(defaultConfig));
            return defaultConfig;
        }

        var text = File.ReadAllText(path);
        return TomlSerializer.Deserialize<GlobalConfig>(text, Options) ?? new GlobalConfig();
    }

    public static void SaveGlobalConfig(string path, GlobalConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, RenderGlobalConfigToml(config));
    }

    public static string RenderGlobalConfigToml(GlobalConfig config)
    {
        var ntfyTopicLine = config.NtfyTopic is not null
            ? $"ntfy_topic = \"{config.NtfyTopic}\""
            : "# ntfy_topic = \"warden-alerts\"";
        var scanPathsJson = JsonSerializer.Serialize(config.ScanPaths);

        return $"""
            # Config global do daemon Warden. Todo campo aqui já tem esse valor por
            # default no código — esse arquivo só existe pra deixar visível o que dá
            # pra mudar, sem precisar ler o código-fonte.

            # Porta onde a API (Kestrel) sobe.
            api_port = {config.ApiPort}

            # Canal de notificação de eventos (started/stopped/finished/error): "none" ou "ntfy".
            notify_channel = "{config.NotifyChannel}"

            # Obrigatório se notify_channel = "ntfy" (sem default — precisa descomentar e preencher).
            {ntfyTopicLine}

            # Server do ntfy — só muda se for self-hosted.
            ntfy_server = "{config.NtfyServer}"

            # Pastas onde o Warden procura projetos candidatos (subpastas diretas) ao
            # sincronizar pelo front — gerenciado por lá, mas editar aqui também vale.
            scan_paths = {scanPathsJson}

            """;
    }
}
