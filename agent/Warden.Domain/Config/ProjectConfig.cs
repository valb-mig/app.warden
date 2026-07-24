using System.Text.Json.Serialization;

namespace Warden.Domain.Config;

public sealed class StartConfig
{
    [JsonRequired]
    public List<string> Cmd { get; init; } = [];
    public string? Cwd { get; init; }
    public bool CaptureStdout { get; init; }
}

public sealed class NotifyConfig
{
    public bool OnError { get; init; }
    public bool OnFinished { get; init; }
    public bool OnGitBehind { get; init; }
}

public sealed class GitWatchConfig
{
    public bool Watch { get; init; }
    public double Interval { get; init; } = 300.0;
    public string Remote { get; init; } = "origin";
}

public sealed class LogSource
{
    [JsonRequired]
    public string Name { get; init; } = "";

    /// <summary>"stdout" | "file" | "docker" — string, não enum, mesma escolha de <see cref="ProjectConfig.Type"/>.</summary>
    [JsonRequired]
    public string Type { get; init; } = "";
    public string? Path { get; init; }
    public string? Service { get; init; }
    public List<string> ErrorPatterns { get; init; } = [];
}

public sealed class ActionConfig
{
    [JsonRequired]
    public string Name { get; init; } = "";
    public List<string> Cmd { get; init; } = [];
    public bool Interactive { get; init; }
    public bool Destructive { get; init; }
    /// <summary>Expressão cron 5-campos (min hour day month weekday) para execução automática. Null = sem agendamento.</summary>
    public string? Cron { get; init; }
}

public sealed class ProjectConfig
{
    /// <summary>
    /// Tipos conhecidos pelo <c>AdapterFactory</c>. <see cref="Type"/> fica como string (não enum) porque
    /// Tomlyn, no default, mapeia enum para número — mapear manualmente evita depender de conversor extra
    /// e mantém o dado igual ao que está no `.toml`, mesma escolha do `Literal[...]` do Pydantic.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownTypes =
        new HashSet<string> { "docker", "node", "python", "php", "just", "raw" };

    [JsonRequired]
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string? Group { get; init; }
    [JsonRequired]
    public string Path { get; init; } = "";
    [JsonRequired]
    public string Type { get; init; } = "";
    public string? ComposeFile { get; init; }
    public List<string> ComposeServices { get; init; } = [];
    public StartConfig? Start { get; init; }
    public NotifyConfig Notify { get; init; } = new();
    public GitWatchConfig Git { get; init; } = new();
    public List<LogSource> LogSources { get; init; } = [];
    public List<ActionConfig> Actions { get; init; } = [];
    /// <summary>Variáveis de ambiente injetadas no processo ao iniciar. Gerenciadas pelo Admin (nunca expostas pelo Console).</summary>
    public Dictionary<string, string> Env { get; init; } = [];

    public string DisplayName => Name ?? Id;
}
