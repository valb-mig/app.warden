using Warden.Contracts.Admin;

namespace Warden.Agent.Admin;

/// <summary>
/// Resolve e protege o unix socket da superfície de Admin (NEW_CONTEXT.md §10.7). Prefere
/// `$XDG_RUNTIME_DIR/warden` (efêmero, já `0700` por convenção do systemd-logind); cai pro
/// `configDir` se a variável não existir. Named pipe do Windows (mesma seção) fica de fora por
/// enquanto — sem máquina Windows real pra validar, e ACL de pipe errada é pior que não ter (ver
/// nota da fase 6 no NEW_CONTEXT.md).
/// </summary>
public static class AdminSocket
{
    public static string ResolvePath(string configDir)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "unix socket do Admin não se aplica no Windows — named pipe (NEW_CONTEXT.md §10.7) ainda não implementado");
        }

        var path = AdminSocketPath.Resolve(configDir);
        var dir = Path.GetDirectoryName(path)!;

        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (File.Exists(path)) File.Delete(path); // socket órfão de um shutdown anterior — Kestrel não sobrescreve sozinho
        return path;
    }

    public static void Secure(string socketPath)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(socketPath))
        {
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
