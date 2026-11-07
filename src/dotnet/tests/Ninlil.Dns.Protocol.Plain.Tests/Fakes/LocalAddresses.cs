using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests.Fakes;

/// <summary>
///     This class finds free ports and usable loopback addresses for the listen-address tests,
///     which use real sockets. Ports always come from the OS, never a fixed number, because CI
///     runners run DNS and DHCP services of their own.
/// </summary>
public static class LocalAddresses
{
    /// <summary>
    ///     This property contains an address from the TEST-NET-1 documentation range (RFC 5737),
    ///     which is not assigned to any host, so binding it fails with "cannot assign requested
    ///     address".
    /// </summary>
    public static IPAddress Unassigned { get; } = IPAddress.Parse("192.0.2.1");

    /// <summary>
    ///     This constant is how many ports <see cref="GetFreePort" /> and the listen-address tests
    ///     try before they give up. It is set above the longest run of refused ports observed on
    ///     Windows (see <see cref="GetFreePort" />).
    /// </summary>
    public const int PortSearchLimit = 2000;

    /// <summary>
    ///     This constant is how many times <see cref="StartOnFreePortAsync{T}" /> starts a host
    ///     before it gives up.
    /// </summary>
    private const int MaximumHostStarts = 10;

    /// <summary>
    ///     This method returns a port that is free for both UDP and TCP on every address, by
    ///     briefly holding it through dual-stack wildcard sockets. The listeners under test bind
    ///     UDP and TCP on one port number across several sockets, which port 0 cannot give, so
    ///     the number has to be reserved first and released before they bind it;
    ///     <see cref="StartOnFreePortAsync{T}" /> retries a host whose bind lost that race.
    ///     <para>
    ///         Windows keeps separate excluded port ranges for UDP and TCP
    ///         (<c>netsh int ipv4 show excludedportrange protocol=tcp</c> lists them) and hands
    ///         out port-0 binds in sequence. A UDP bind to port 0 can therefore return a port
    ///         inside a TCP-excluded range, the following UDP ports lie in the same range, and
    ///         the TCP bind of the same number is refused with "access permissions" (WSAEACCES)
    ///         until the sequence leaves it. On one Windows test host the TCP list held five
    ///         adjacent 100-port ranges (51111 to 51610), and a probe saw runs of refused
    ///         pairs lasting up to about 8 seconds. A refused pair is skipped at once, with no
    ///         pause, and the search goes on until <see cref="PortSearchLimit" />, which is set
    ///         well above such a run.
    ///     </para>
    /// </summary>
    public static int GetFreePort()
    {
        for (int attempt = 1; attempt <= PortSearchLimit; attempt++)
        {
            try
            {
                using Socket udp = CreateWildcardSocket(SocketType.Dgram, ProtocolType.Udp);
                udp.Bind(new IPEndPoint(WildcardAddress, 0));
                int port = ((IPEndPoint)udp.LocalEndPoint).Port;

                using Socket tcp = CreateWildcardSocket(SocketType.Stream, ProtocolType.Tcp);
                tcp.Bind(new IPEndPoint(WildcardAddress, port));
                return port;
            }
            catch (SocketException)
            {
                // The TCP side is taken or excluded for this number, or the UDP bind itself was
                // refused for the moment; ask for the next port.
            }
        }

        throw new InvalidOperationException("Could not find a port free for both UDP and TCP.");
    }

    /// <summary>
    ///     This method starts a host on a port from <see cref="GetFreePort" /> and starts it again
    ///     on another port when the bind failed because something took the port after it was
    ///     released.
    /// </summary>
    /// <param name="tryStart">
    ///     Starts the host on the given port. It returns <see langword="null" /> after cleaning up
    ///     when the listeners could not bind that port.
    /// </param>
    public static async Task<T> StartOnFreePortAsync<T>(Func<int, Task<T>> tryStart)
        where T : class
    {
        for (int attempt = 1; attempt <= MaximumHostStarts; attempt++)
        {
            T host = await tryStart(GetFreePort());

            if (host is not null)
                return host;
        }

        throw new InvalidOperationException("No free port could be bound by the listeners.");
    }

    /// <summary>
    ///     This method returns a second loopback address distinct from 127.0.0.1 that this host can
    ///     bind: <c>::1</c> when IPv6 is available, otherwise <c>127.0.0.2</c> (Linux routes all of
    ///     127.0.0.0/8 to loopback), or <see langword="null" /> when neither works.
    /// </summary>
    public static IPAddress OtherLoopback() =>
        CanBind(IPAddress.IPv6Loopback) ? IPAddress.IPv6Loopback
        : CanBind(IPAddress.Parse("127.0.0.2")) ? IPAddress.Parse("127.0.0.2")
        : null;

    /// <summary>This method reports whether a UDP socket can be bound to an address.</summary>
    public static bool CanBind(IPAddress address)
    {
        try
        {
            using Socket socket = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(address, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static IPAddress WildcardAddress =>
        Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;

    private static Socket CreateWildcardSocket(SocketType socketType, ProtocolType protocolType)
    {
        Socket socket = new(WildcardAddress.AddressFamily, socketType, protocolType);

        if (WildcardAddress.AddressFamily == AddressFamily.InterNetworkV6)
            socket.DualMode = true;

        return socket;
    }
}
