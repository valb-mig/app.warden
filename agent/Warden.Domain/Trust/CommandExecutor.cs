using System.Diagnostics;

namespace Warden.Domain.Trust;

/// <summary>
/// Executa um <see cref="ManifestCommand"/> até o fim, capturando stdout+stderr — usado por ações
/// não-interativas disparadas via API (equivalente ao `subprocess.run(..., timeout=300)` do engine
/// Python). Comandos de longa duração (ex: `start`) não passam por aqui — esses são donos de
/// processo via <c>IAdapter</c>, não execução síncrona.
/// </summary>
public static class CommandExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    public static ActionExecutionResult Run(ManifestCommand command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command.Argv[0],
            WorkingDirectory = command.Cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in command.Argv.Skip(1)) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"comando \"{command.Name}\" excedeu {Timeout.TotalSeconds}s");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return new ActionExecutionResult { ExitCode = process.ExitCode, Output = stdout + stderr };
    }
}
