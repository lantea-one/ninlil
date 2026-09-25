using System.Runtime.InteropServices;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence.Netlink;

/// <summary>
///     This interface reads a snapshot of the kernel's neighbor table.
/// </summary>
public interface INeighborTableReader
{
    /// <summary>
    ///     This method reads every current neighbor entry, or returns <see langword="null" /> when
    ///     the neighbor table cannot be read on this platform at all.
    /// </summary>
    /// <returns>The neighbor entries, or <see langword="null" /> when unsupported.</returns>
    IReadOnlyList<NeighborTableEntry> Read();
}

/// <summary>
///     This class reads the kernel neighbor table (both IPv4 ARP and IPv6 NDP entries) over a
///     Linux rtnetlink socket, with one <c>RTM_GETNEIGH</c> dump request. Unlike
///     <c>/proc/net/arp</c>, rtnetlink reports each entry's reachability state, which lets
///     presence tracking tell a device that is here now from one whose stale entry the kernel has
///     not evicted.
///     <para>
///         .NET's managed <see cref="System.Net.Sockets.Socket" /> refuses the netlink address
///         family outright, so this class calls the C library's socket functions directly. Only the
///         Linux 64-bit targets this project builds for are supported; on any other platform, or if
///         the socket cannot be opened (a sandbox forbidding netlink, for example), <see cref="Read" />
///         returns <see langword="null" /> and presence tracking goes without this signal.
///     </para>
/// </summary>
public sealed partial class NetlinkNeighborTableReader : INeighborTableReader
{
    /// <summary>The Linux address family for netlink sockets (<c>AF_NETLINK</c>).</summary>
    private const int AddressFamilyNetlink = 16;

    /// <summary>A raw socket (<c>SOCK_RAW</c>) closed across exec (<c>SOCK_CLOEXEC</c>).</summary>
    private const int SocketTypeRawCloseOnExec = 3 | 0x80000;

    /// <summary>The routing netlink protocol (<c>NETLINK_ROUTE</c>).</summary>
    private const int ProtocolNetlinkRoute = 0;

    /// <summary>The generic socket option level (<c>SOL_SOCKET</c>).</summary>
    private const int SocketOptionLevelSocket = 1;

    /// <summary>The receive-timeout socket option (<c>SO_RCVTIMEO</c>).</summary>
    private const int SocketOptionReceiveTimeout = 20;

    /// <summary>How long one receive may block before the dump is abandoned, in seconds.</summary>
    private const long ReceiveTimeoutSeconds = 2;

    /// <summary>An upper bound on datagrams per dump, guarding against a reply that never ends.</summary>
    private const int MaximumDatagrams = 4096;

    /// <summary>The sequence number stamped on each request.</summary>
    private uint _sequence;

    /// <inheritdoc />
    public IReadOnlyList<NeighborTableEntry> Read()
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess)
            return null;

        int socket;
        try
        {
            socket = OpenSocket(
                AddressFamilyNetlink,
                SocketTypeRawCloseOnExec,
                ProtocolNetlinkRoute
            );
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        if (socket < 0)
            return null;

        try
        {
            // A struct timeval on a 64-bit Linux target: two 8-octet fields, seconds then microseconds.
            long[] timeout = [ReceiveTimeoutSeconds, 0];
            SetSocketOption(
                socket,
                SocketOptionLevelSocket,
                SocketOptionReceiveTimeout,
                timeout,
                sizeof(long) * 2
            );

            byte[] request = NetlinkNeighborMessageParser.BuildDumpRequest(
                Interlocked.Increment(ref _sequence)
            );
            // An unconnected netlink socket sends to the kernel (port id 0) by default.
            if (Send(socket, request, request.Length, 0) != request.Length)
                return null;

            List<NeighborTableEntry> entries = [];
            byte[] buffer = new byte[64 * 1024];
            for (int datagram = 0; datagram < MaximumDatagrams; datagram++)
            {
                nint received = Receive(socket, buffer, buffer.Length, 0);

                // A timeout or error mid-dump means an incomplete picture; report nothing rather
                // than a partial table that would make present devices look absent.
                if (received <= 0)
                    return null;

                if (
                    NetlinkNeighborMessageParser.ParseDatagram(
                        buffer.AsSpan(0, (int)received),
                        entries
                    )
                )
                    return entries;
            }

            return null;
        }
        finally
        {
            Close(socket);
        }
    }

    /// <summary>Opens a socket (<c>socket(2)</c>).</summary>
    [LibraryImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static partial int OpenSocket(int domain, int type, int protocol);

    /// <summary>Sets a socket option (<c>setsockopt(2)</c>).</summary>
    [LibraryImport("libc", EntryPoint = "setsockopt", SetLastError = true)]
    private static partial int SetSocketOption(
        int socket,
        int level,
        int optionName,
        long[] optionValue,
        int optionLength
    );

    /// <summary>Sends a datagram (<c>send(2)</c>).</summary>
    [LibraryImport("libc", EntryPoint = "send", SetLastError = true)]
    private static partial nint Send(int socket, byte[] buffer, nint length, int flags);

    /// <summary>Receives a datagram (<c>recv(2)</c>).</summary>
    [LibraryImport("libc", EntryPoint = "recv", SetLastError = true)]
    private static partial nint Receive(int socket, byte[] buffer, nint length, int flags);

    /// <summary>Closes a file descriptor (<c>close(2)</c>).</summary>
    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fileDescriptor);
}
