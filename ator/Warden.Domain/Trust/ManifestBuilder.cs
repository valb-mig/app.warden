using Warden.Domain.Config;

namespace Warden.Domain.Trust;

/// <summary>
/// Resolve o `CommandManifest` de um projeto a partir do seu `ProjectConfig` — hoje só `[start]` +
/// `[[actions]]` (o que a config já declara explicitamente); descoberta automática de scripts
/// (`package.json`/`composer.json`/Justfile) é a mesma pendência do `scaffold.py`/`discovery.py`
/// ainda não portados, fica pra quando essa fase entrar.
/// </summary>
public static class ManifestBuilder
{
    public static CommandManifest Build(ProjectConfig config)
    {
        var commands = new List<ManifestCommand>();

        if (config.Start is { } start)
        {
            commands.Add(new ManifestCommand
            {
                Name = "start",
                Argv = start.Cmd,
                Cwd = start.Cwd ?? config.Path,
            });
        }

        foreach (var action in config.Actions)
        {
            commands.Add(new ManifestCommand
            {
                Name = action.Name,
                Argv = action.Cmd,
                Cwd = config.Path,
                Interactive = action.Interactive,
                Destructive = action.Destructive,
            });
        }

        return new CommandManifest { ProjectId = config.Id, Commands = commands };
    }
}
