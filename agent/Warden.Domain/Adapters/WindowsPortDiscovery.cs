using System.Runtime.InteropServices;

namespace Warden.Domain.Adapters;

/// <summary>
/// Lista portas TCP em LISTEN de um PID via `GetExtendedTcpTable`/`MIB_TCPTABLE_OWNER_PID`
/// (iphlpapi.dll) — API estável desde o Windows XP SP2, o mesmo mecanismo que `netstat -ano` usa.
///
/// ATENÇÃO: implementado com base na documentação da API, mas não validado numa máquina Windows
/// real nesta sessão (ambiente de dev é Linux/Arch) — ver NEW_CONTEXT.md §12 fase 2. Validar antes
/// de confiar cegamente em produção Windows.
/// </summary>
internal sealed class WindowsPortDiscovery : IPortDiscovery
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3; // TCP_TABLE_OWNER_PID_LISTENER

    public IReadOnlyList<int> ListeningPorts(int pid)
    {
        var ports = new SortedSet<int>();
        CollectIPv4(pid, ports);
        CollectIPv6(pid, ports);
        return ports.ToList();
    }

    private static void CollectIPv4(int pid, SortedSet<int> ports)
    {
        var buffer = QueryTable(AfInet);
        if (buffer is null) return;
        try
        {
            var rowCount = Marshal.ReadInt32(buffer.Value.Ptr);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var rowPtr = buffer.Value.Ptr + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr + i * rowSize);
                if (row.OwningPid == pid)
                {
                    ports.Add(SwapPortByteOrder(row.LocalPort));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer.Value.Ptr);
        }
    }

    private static void CollectIPv6(int pid, SortedSet<int> ports)
    {
        var buffer = QueryTable(AfInet6);
        if (buffer is null) return;
        try
        {
            var rowCount = Marshal.ReadInt32(buffer.Value.Ptr);
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            var rowPtr = buffer.Value.Ptr + 4;
            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr + i * rowSize);
                if (row.OwningPid == pid)
                {
                    ports.Add(SwapPortByteOrder(row.LocalPort));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer.Value.Ptr);
        }
    }

    private static (nint Ptr, int Size)? QueryTable(int ipVersion)
    {
        var size = 0;
        _ = GetExtendedTcpTable(0, ref size, false, ipVersion, TcpTableOwnerPidListener, 0);
        if (size <= 0) return null;

        var ptr = Marshal.AllocHGlobal(size);
        var result = GetExtendedTcpTable(ptr, ref size, false, ipVersion, TcpTableOwnerPidListener, 0);
        if (result != 0)
        {
            Marshal.FreeHGlobal(ptr);
            return null;
        }
        return (ptr, size);
    }

    /// <summary>dwLocalPort vem em network byte order dentro do DWORD (baixo 16 bits) — precisa inverter os bytes.</summary>
    private static int SwapPortByteOrder(uint rawPort) =>
        ((int)(rawPort & 0xFF) << 8) | (int)((rawPort >> 8) & 0xFF);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        nint tcpTable, ref int size, bool sort, int ipVersion, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }
}
