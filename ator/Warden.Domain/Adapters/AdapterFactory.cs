using Warden.Domain.Config;

namespace Warden.Domain.Adapters;

/// <summary>Detecção de adapter pelo campo <c>type</c> do config.</summary>
public static class AdapterFactory
{
    public static IAdapter Create(ProjectConfig config) => config.Type switch
    {
        "python" => new PythonAdapter(config),
        "raw" => new RawAdapter(config),
        "docker" => new DockerAdapter(config),
        "node" => new NodeAdapter(config),
        "php" => new PhpAdapter(config),
        "just" => new JustAdapter(config),
        _ => throw new NotSupportedException(
            $"adapter para type=\"{config.Type}\" ainda não implementado (fase futura)"),
    };
}
