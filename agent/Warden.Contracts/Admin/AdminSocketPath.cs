namespace Warden.Contracts.Admin;

/// <summary>
/// Resolve o caminho do unix socket do Admin (NEW_CONTEXT.md §10.7) — usado pelos dois lados: o
/// Agent (que cria/protege o arquivo) e o Admin (que só conecta). Precisa ser a mesma regra nos
/// dois processos, senão cada um calcula um caminho diferente e nunca se encontram.
/// </summary>
public static class AdminSocketPath
{
    public static string Resolve(string configDir)
    {
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var dir = !string.IsNullOrEmpty(xdgRuntimeDir) ? Path.Combine(xdgRuntimeDir, "warden") : configDir;
        return Path.Combine(dir, "admin.sock");
    }
}
