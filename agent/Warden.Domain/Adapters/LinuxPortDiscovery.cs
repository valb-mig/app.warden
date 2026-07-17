using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Warden.Domain.Adapters;

/// <summary>
/// Lista portas TCP em LISTEN de um PID cruzando `/proc/[pid]/fd/*` (symlinks mágicos tipo
/// `socket:[12345]`) com `/proc/net/tcp`+`tcp6` (que trazem inode + estado + porta de toda socket
/// do sistema). Mesma técnica que `ss`/`lsof` usam por baixo dos panos.
/// </summary>
internal sealed class LinuxPortDiscovery : IPortDiscovery
{
    private const string ListenState = "0A";

    public IReadOnlyList<int> ListeningPorts(int pid)
    {
        var inodes = CollectSocketInodes(pid);
        if (inodes.Count == 0) return [];

        var ports = new SortedSet<int>();
        CollectFromTcpTable("/proc/net/tcp", inodes, ports);
        CollectFromTcpTable("/proc/net/tcp6", inodes, ports);
        return ports.ToList();
    }

    private static HashSet<string> CollectSocketInodes(int pid)
    {
        var inodes = new HashSet<string>();
        var fdDir = $"/proc/{pid}/fd";
        if (!Directory.Exists(fdDir)) return inodes;

        IEnumerable<string> fdEntries;
        try
        {
            fdEntries = Directory.EnumerateFileSystemEntries(fdDir).ToList();
        }
        catch (IOException)
        {
            return inodes; // processo morreu entre o Directory.Exists e o enumerate
        }
        catch (UnauthorizedAccessException)
        {
            return inodes; // sem permissão de ler fd de processo de outro usuário
        }

        foreach (var fd in fdEntries)
        {
            var target = LinuxReadlink.Read(fd);
            if (target is null) continue;

            if (target.StartsWith("socket:[", StringComparison.Ordinal) && target.EndsWith(']'))
            {
                inodes.Add(target[8..^1]);
            }
        }

        return inodes;
    }

    private static void CollectFromTcpTable(string path, HashSet<string> inodes, SortedSet<int> ports)
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10) continue;

            var state = fields[3];
            var inode = fields[9];
            if (!string.Equals(state, ListenState, StringComparison.OrdinalIgnoreCase)) continue;
            if (!inodes.Contains(inode)) continue;

            var localAddress = fields[1];
            var portHex = localAddress.Split(':').Last();
            if (int.TryParse(portHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port))
            {
                ports.Add(port);
            }
        }
    }
}

/// <summary>
/// `readlink()` cru via libc. Não dá pra usar `File.ResolveLinkTarget` aqui: o alvo de
/// `/proc/[pid]/fd/N` não é um path real (é `socket:[12345]`/`pipe:[678]`), e a API de alto nível
/// do .NET tentaria recompor isso como path relativo ao diretório do link, corrompendo o valor.
/// </summary>
internal static class LinuxReadlink
{
    [DllImport("libc", SetLastError = true, EntryPoint = "readlink")]
    private static extern nint NativeReadlink(string pathname, byte[] buf, nint bufsiz);

    public static string? Read(string path)
    {
        var buffer = new byte[4096];
        nint result;
        try
        {
            result = NativeReadlink(path, buffer, buffer.Length);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        if (result < 0) return null;
        return Encoding.UTF8.GetString(buffer, 0, (int)result);
    }
}
