using System.Runtime.InteropServices;

namespace Warden.Domain.Adapters;

/// <summary>
/// Resolve um nome de comando (`"python"`) pro caminho absoluto do executável via `PATH`. Necessário
/// porque, ao contrário de `System.Diagnostics.Process`/`execvp`, o `PtyProvider.SpawnAsync` do
/// Porta.Pty espera `App` já resolvido (o próprio exemplo da lib usa caminho absoluto).
/// </summary>
internal static class ExecutableResolver
{
    public static string Resolve(string command)
    {
        if (command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar)
            || Path.IsPathRooted(command))
        {
            return command;
        }

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = isWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [""];

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return command; // deixa o SO tentar (e falhar com erro claro) se não achou
    }
}
