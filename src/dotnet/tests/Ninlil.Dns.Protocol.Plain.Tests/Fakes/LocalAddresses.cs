using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests.Fakes;

/// <summary>
///     This class finds free ports and usable loopback addresses for the listen-address tests,
///     which use real sockets. Ports are drawn at random from the dynamic range, never a fixed
///     number, because CI runners run DNS and DHCP services of their own.
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
    ///     This constant is how many times <see cref="StartOnFreePortAsync{T}" /> starts a host
    ///     before it gives up.
    /// </summary>
    private const int MaximumHostStarts = 10;

    /// <summary>
    ///     This constant is the most ports <see cref="GetFreePort" /> tries before it gives up.
    /// </summary>
    public const int PortSearchLimit = 2000;

    /// <summary>
    ///     This method returns a port that is free for both UDP and TCP on every address, by
    ///     briefly holding it through dual-stack wildcard sockets. The caller binds it after this
    ///     returns, so the listener tests that need one number for UDP, TCP or several addresses
    ///     (port 0 gives each socket its own) use it, and a test that can bind port 0 and read the
    ///     port back does that instead.
    ///     <para>
    ///         The port is picked at random from the dynamic range (49152 to 65535) and bound
    ///         explicitly. It is not taken from a port-0 bind, because Windows hands out port-0
    ///         ports one after the other from a counter the whole machine shares, and keeps
    ///         separate excluded ranges for UDP and TCP
    ///         (<c>netsh int ipv4 show excludedportrange protocol=tcp</c> lists them). A port-0
    ///         port inside an excluded block of the other protocol is refused on the second bind
    ///         (WSAEACCES), and so is every following port until the counter leaves the block,
    ///         which can be several hundred ports. A search that follows the counter fails
    ///         together with every other caller that runs while the counter sits in the block.
    ///         Random picks spread over the whole range, so a refused pick costs one attempt.
    ///         <see cref="PortSearchLimit" /> is far above what the excluded blocks need.
    ///     </para>
    /// </summary>
    public static int GetFreePort()
    {
        for (int attempt = 1; attempt <= PortSearchLimit; attempt++)
        {
            int port = Random.Shared.Next(DynamicPortStart, DynamicPortEnd + 1);

            try
            {
                using Socket tcp = CreateWildcardSocket(SocketType.Stream, ProtocolType.Tcp);
                tcp.Bind(new IPEndPoint(WildcardAddress, port));

                using Socket udp = CreateWildcardSocket(SocketType.Dgram, ProtocolType.Udp);
                udp.Bind(new IPEndPoint(WildcardAddress, port));

                return port;
            }
            catch (SocketException)
            {
                // In use, or excluded for one of the two protocols; pick another.
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

    /// <summary>
    ///     This constant is the first port of the IANA dynamic range (RFC 6335 section 6), which
    ///     <see cref="GetFreePort" /> draws from.
    /// </summary>
    private const int DynamicPortStart = 49152;

    /// <summary>This constant is the last port of the dynamic range.</summary>
    private const int DynamicPortEnd = 65535;

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
