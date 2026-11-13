using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Ninlil.Common.Extension.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class binds the listening sockets for one socket-based DNS transport (plain UDP/TCP,
///     DNS-over-TLS, DNSCrypt UDP/TCP) from its <c>Port</c> and <c>ListenAddresses</c> settings,
///     and keeps them together so the listener can serve and later close them as one unit.
///     <para>
///         With no addresses configured, this binds one IPv6 wildcard socket with
///         <see cref="SocketOptionName.IPv6Only" /> turned off, which accepts IPv6 and (through the
///         operating system's IPv4-mapped addresses) IPv4 clients on every interface. A failure
///         to bind it is logged and leaves the set empty (see the Windows paragraph below for the
///         one case that falls back).
///     </para>
///     <para>
///         With addresses configured, this binds one socket per address. Every IPv6 socket is
///         IPv6-only, so <c>::</c> claims only IPv6 and can sit next to <c>0.0.0.0</c> on the same
///         port. An address that cannot be bound (it isn't assigned to this host, another program
///         already owns that address and port, ...) is logged as an error naming the transport and
///         the address, recorded in <see cref="FailedAddresses" /> for the <c>/status</c> health
///         check, and skipped. The other addresses keep serving and nothing is thrown, so one
///         stale address cannot take the whole server down.
///     </para>
///     <para>
///         On Windows every socket claims its port exclusively (see
///         <see cref="ExclusiveListenerPortSocketExtension" />). With addresses configured, a
///         program that holds the same address and port makes that bind fail, and the failure is
///         reported as above. With no addresses configured, a failed wildcard bind (another
///         program holds the port on some address, typically UDP 53 held by connection sharing,
///         Hyper-V or WSL) falls back to one exclusive socket per usable local unicast address,
///         loopback included, with one warning. Addresses that fail are listed in
///         <see cref="FailedAddresses" />; addresses the machine gains later are not served until
///         the transport restarts. When no address binds, the set is empty and an error is logged.
///     </para>
/// </summary>
public sealed class ListenSocketSet
{
    /// <summary>
    ///     This constructor is private: <see cref="Bind" /> is the only way to create a set.
    /// </summary>
    private ListenSocketSet(IReadOnlyList<Socket> sockets, IReadOnlyList<string> failedAddresses)
    {
        Sockets = sockets;
        FailedAddresses = failedAddresses;
        BoundPort = sockets.Count == 0 ? 0 : ((IPEndPoint)sockets[0].LocalEndPoint).Port;
    }

    /// <summary>
    ///     This property contains an empty set, for a listener that has not bound anything yet, so
    ///     a status read before the first bind sees "nothing failed" rather than null.
    /// </summary>
    public static ListenSocketSet Empty { get; } = new([], []);

    /// <summary>
    ///     This property contains the port of the first bound socket, or 0 when nothing bound. It
    ///     is how a caller that asked for port 0 learns the port the operating system chose. With
    ///     several sockets on port 0 each gets a port of its own, so it names only the first.
    /// </summary>
    public int BoundPort { get; }

    /// <summary>This property contains every socket that bound successfully.</summary>
    public IReadOnlyList<Socket> Sockets { get; }

    /// <summary>
    ///     This property describes every configured address that is not being served, each as
    ///     "address (reason)": an entry that is not an IP address, or one that failed to bind.
    /// </summary>
    public IReadOnlyList<string> FailedAddresses { get; }

    /// <summary>
    ///     This method binds every socket the transport's settings ask for. A TCP socket is also
    ///     put into the listening state; a UDP socket is ready to receive once this returns. On
    ///     Windows, a listen-everywhere set whose exclusive wildcard bind fails with "address in
    ///     use" or "access denied" binds each usable local address instead (see the class
    ///     summary).
    /// </summary>
    /// <param name="transportName">The transport's human-readable name, used in log messages (for example "Plain DNS (UDP)").</param>
    /// <param name="configuredAddresses">The transport's <c>ListenAddresses</c> value.</param>
    /// <param name="port">The port every socket binds.</param>
    /// <param name="protocolType"><see cref="ProtocolType.Udp" /> or <see cref="ProtocolType.Tcp" />.</param>
    /// <param name="logger">The listener's logger, which bind problems are reported to.</param>
    /// <returns>The bound sockets, plus every address that could not be served.</returns>
    public static ListenSocketSet Bind(
        string transportName,
        string configuredAddresses,
        int port,
        ProtocolType protocolType,
        ILogger logger
    )
    {
        ListenAddressResolution resolution = ListenAddressList.Resolve(configuredAddresses);

        if (resolution.ListensEverywhere)
        {
            Socket dualStack = CreateSocket(AddressFamily.InterNetworkV6, protocolType);

            try
            {
                dualStack.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                dualStack.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

                if (protocolType == ProtocolType.Tcp)
                    dualStack.Listen();
            }
            catch (SocketException exception)
            {
                // A listener that throws out of ExecuteAsync stops the whole host, taking the
                // other transports, DHCP and the REST API with it, and the operator could not
                // reach the settings to fix the port. Report it instead: the listener ends
                // without sockets, so the transports health check shows it as not listening.
                dualStack.Dispose();

                // On Windows another program may hold the port on some addresses (typically a
                // DNS proxy such as Internet Connection Sharing on UDP 53). The exclusive
                // wildcard bind then fails, but each local address is usually still free.
                if (
                    OperatingSystem.IsWindows()
                    && exception.SocketErrorCode
                        is SocketError.AddressAlreadyInUse
                            or SocketError.AccessDenied
                    && TryBindLocalAddresses(
                        transportName,
                        port,
                        protocolType,
                        logger,
                        exception,
                        out ListenSocketSet fallback
                    )
                )
                    return fallback;

                logger.LogError(
                    exception,
                    "{Transport} could not listen on {Address} port {Port}: {Reason}. This transport is not running; the rest of the server is unaffected. Fix the port or the listen addresses and restart Ninlil.",
                    transportName,
                    IPAddress.IPv6Any,
                    port,
                    exception.Message
                );
                return new ListenSocketSet([], [$"{IPAddress.IPv6Any} ({exception.Message})"]);
            }
            catch
            {
                dualStack.Dispose();
                throw;
            }

            return new ListenSocketSet([dualStack], []);
        }

        List<string> failed = ReportUnusableEntries(transportName, resolution, logger);
        List<Socket> sockets = BindEachAddress(
            transportName,
            resolution.Addresses,
            port,
            protocolType,
            logger,
            failed,
            "Its other listen addresses keep serving."
        );

        ReportNothingBound(transportName, sockets.Count, logger);
        return new ListenSocketSet(sockets, failed);
    }

    /// <summary>
    ///     This method resolves the endpoints a transport that can't use a plain socket
    ///     (DNS-over-QUIC, whose QUIC library creates its own sockets) should listen on, applying
    ///     the same rules and reporting as <see cref="Bind" />. With no addresses configured it
    ///     returns the single IPv6 wildcard endpoint.
    /// </summary>
    /// <param name="transportName">The transport's human-readable name, used in log messages.</param>
    /// <param name="configuredAddresses">The transport's <c>ListenAddresses</c> value.</param>
    /// <param name="port">The port every endpoint uses.</param>
    /// <param name="logger">The listener's logger, which unusable entries are reported to.</param>
    /// <param name="failedAddresses">Every configured entry that will not be served, each as "address (reason)".</param>
    /// <returns>The endpoints to listen on.</returns>
    public static IReadOnlyList<IPEndPoint> ResolveEndpoints(
        string transportName,
        string configuredAddresses,
        int port,
        ILogger logger,
        out List<string> failedAddresses
    )
    {
        ListenAddressResolution resolution = ListenAddressList.Resolve(configuredAddresses);

        if (resolution.ListensEverywhere)
        {
            failedAddresses = [];
            return [new IPEndPoint(IPAddress.IPv6Any, port)];
        }

        failedAddresses = ReportUnusableEntries(transportName, resolution, logger);
        return [.. resolution.Addresses.Select(address => new IPEndPoint(address, port))];
    }

    /// <summary>
    ///     This method reports whether an address is assigned to one of this host's network
    ///     interfaces. Wildcards always count as assigned. Needed for DNS-over-QUIC only: its QUIC
    ///     library binds a wildcard socket underneath and filters by address itself, so listening
    ///     on an address this host doesn't have "succeeds" and then silently never answers.
    /// </summary>
    public static bool IsAssignedToThisHost(IPAddress address)
    {
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        try
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Any(unicast => SameAddress(unicast.Address, address));
        }
        catch (NetworkInformationException)
        {
            // Without an interface list there's nothing to check against; let the bind itself
            // decide rather than refusing an address that may well be fine.
            return true;
        }
    }

    /// <summary>
    ///     This method logs an error when a listener with configured addresses bound none of
    ///     them. DNS-over-QUIC, which binds its endpoints itself, calls it too so it reports the
    ///     same way <see cref="Bind" /> does.
    /// </summary>
    public static void ReportNothingBound(string transportName, int boundCount, ILogger logger)
    {
        if (boundCount == 0)
            logger.LogError(
                "{Transport} has listen addresses configured, but none of them could be used, so it is not listening anywhere. Fix the addresses, or clear the list to listen on every address.",
                transportName
            );
    }

    /// <summary>This method closes every socket in this set, unblocking any pending receive or accept on it.</summary>
    public void Close()
    {
        foreach (Socket socket in Sockets)
            socket.Close();
    }

    /// <summary>
    ///     This method logs every configured entry that won't be served before anything is bound:
    ///     one that is not an IP address (an error), and one a same-family wildcard already covers
    ///     (a warning; the wildcard socket still answers its traffic).
    /// </summary>
    /// <returns>The unusable entries, each as "entry (reason)", to start the failure list with.</returns>
    private static List<string> ReportUnusableEntries(
        string transportName,
        ListenAddressResolution resolution,
        ILogger logger
    )
    {
        List<string> failed = [];

        foreach (string entry in resolution.InvalidEntries)
        {
            failed.Add($"{entry} (not an IP address)");
            logger.LogError(
                "{Transport} listen address '{Entry}' is not a valid IP address and is ignored. Only IPv4 and IPv6 literals are accepted -- no host names, interface names or ports.",
                transportName,
                entry
            );
        }

        foreach (IPAddress address in resolution.CoveredAddresses)
            logger.LogWarning(
                "{Transport} listen address {Address} is already covered by the wildcard address of the same family in the list, so it is not bound separately.",
                transportName,
                address
            );

        return failed;
    }

    /// <summary>
    ///     This method binds one socket per address, the way an explicit <c>ListenAddresses</c>
    ///     list asks for. An address that cannot be bound is logged as an error, added to
    ///     <paramref name="failed" /> and skipped.
    /// </summary>
    /// <param name="transportName">The transport's name for log messages.</param>
    /// <param name="addresses">The addresses to bind, each with its own socket.</param>
    /// <param name="port">The port every socket binds.</param>
    /// <param name="protocolType"><see cref="ProtocolType.Udp" /> or <see cref="ProtocolType.Tcp" />.</param>
    /// <param name="logger">The logger bind errors go to.</param>
    /// <param name="failed">The list that receives "address (reason)" for each failure.</param>
    /// <param name="consequence">The closing sentence of the error message.</param>
    /// <returns>The sockets that bound.</returns>
    private static List<Socket> BindEachAddress(
        string transportName,
        IEnumerable<IPAddress> addresses,
        int port,
        ProtocolType protocolType,
        ILogger logger,
        List<string> failed,
        string consequence
    )
    {
        List<Socket> sockets = [];

        foreach (IPAddress address in addresses)
        {
            Socket socket = CreateSocket(address.AddressFamily, protocolType);

            try
            {
                // An explicit IPv6 socket must be IPv6-only: left dual-stack, "::" would also
                // claim every IPv4 address and collide with a "0.0.0.0" entry on the same port.
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);

                socket.Bind(new IPEndPoint(address, port));

                if (protocolType == ProtocolType.Tcp)
                    socket.Listen();

                sockets.Add(socket);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                failed.Add($"{address} ({exception.Message})");
                logger.LogError(
                    exception,
                    "{Transport} could not listen on {Address} port {Port}: {Reason}. {Consequence}",
                    transportName,
                    address,
                    port,
                    exception.Message,
                    consequence
                );
            }
        }

        return sockets;
    }

    /// <summary>
    ///     This method is the Windows fallback for a listen-everywhere transport whose exclusive
    ///     wildcard bind failed: it binds every usable local unicast address on its own and logs
    ///     one warning. The set is usable when at least one address bound. Replies need nothing
    ///     extra: each listener answers from the socket that received the request, so a reply
    ///     leaves from that socket's address, as with an explicit address list.
    /// </summary>
    /// <param name="transportName">The transport's name for log messages.</param>
    /// <param name="port">The port every socket binds.</param>
    /// <param name="protocolType"><see cref="ProtocolType.Udp" /> or <see cref="ProtocolType.Tcp" />.</param>
    /// <param name="logger">The logger the warning and bind errors go to.</param>
    /// <param name="wildcardFailure">The error of the failed wildcard bind.</param>
    /// <param name="set">The per-address set, when this method returns <see langword="true" />.</param>
    /// <returns>Whether at least one address bound.</returns>
    [SupportedOSPlatform("windows")]
    private static bool TryBindLocalAddresses(
        string transportName,
        int port,
        ProtocolType protocolType,
        ILogger logger,
        SocketException wildcardFailure,
        out ListenSocketSet set
    )
    {
        List<string> failed = [];
        List<Socket> sockets = BindEachAddress(
            transportName,
            EnumerateBindableLocalAddresses(),
            port,
            protocolType,
            logger,
            failed,
            "The transport's other local addresses keep serving."
        );

        if (sockets.Count == 0)
        {
            set = null;
            return false;
        }

        logger.LogWarning(
            "{Transport} could not listen on the wildcard address {Address} {Protocol} port {Port} ({Reason}): another program holds that port on some address. Ninlil listens on these local addresses instead: {Bound}. An address added to this machine later is not served until the transport restarts. Find the other program with '{Command}', or set the transport's listen addresses.",
            transportName,
            IPAddress.IPv6Any,
            protocolType.ToString().ToUpperInvariant(),
            port,
            wildcardFailure.Message,
            string.Join(", ", sockets.Select(socket => ((IPEndPoint)socket.LocalEndPoint).Address)),
            $"netstat -ano | findstr :{port}"
        );
        set = new ListenSocketSet(sockets, failed);
        return true;
    }

    /// <summary>
    ///     This method lists the local unicast addresses worth binding: every address of an
    ///     interface that is up, loopback included, IPv4 and IPv6. An IPv6 link-local address
    ///     keeps its scope id (the interface index), which the bind needs. An address whose
    ///     duplicate-address-detection state is not
    ///     <see cref="DuplicateAddressDetectionState.Preferred" /> is skipped: a tentative
    ///     address cannot be bound yet, a duplicate or invalid one is unusable, and a deprecated
    ///     one is no longer chosen for new traffic. Repeated addresses are dropped.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<IPAddress> EnumerateBindableLocalAddresses()
    {
        List<IPAddress> addresses = [];

        try
        {
            foreach (
                NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            )
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (
                    UnicastIPAddressInformation unicast in networkInterface
                        .GetIPProperties()
                        .UnicastAddresses
                )
                {
                    if (
                        unicast.DuplicateAddressDetectionState
                        != DuplicateAddressDetectionState.Preferred
                    )
                        continue;

                    if (!addresses.Contains(unicast.Address))
                        addresses.Add(unicast.Address);
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Without an interface list there is nothing to fall back to; the caller reports the
            // original wildcard failure.
        }

        return addresses;
    }

    /// <summary>This method creates an unbound socket of the right shape for the protocol.</summary>
    private static Socket CreateSocket(AddressFamily family, ProtocolType protocolType)
    {
        Socket socket = new(
            family,
            protocolType == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram,
            protocolType
        );

        // A listening UDP socket receives from many clients. On Windows a client that closes its
        // port before the reply would otherwise fail the next receive with WSAECONNRESET, a
        // warning in the log that an off-path sender can provoke at will.
        if (protocolType == ProtocolType.Udp)
            socket.IgnoreIcmpPortUnreachable();

        // On Windows no other socket may share the port, whether it binds a wildcard or a
        // specific address. A conflict then fails the bind, which Bind reports as a failed
        // address, instead of leaving Ninlil up beside a socket that takes some clients' traffic.
        socket.ClaimPortExclusivelyOnWindows();

        return socket;
    }

    /// <summary>
    ///     This method compares a host address with a configured one, ignoring the IPv6 scope when
    ///     the configured address didn't give one (<c>fe80::1</c> matches <c>fe80::1%eth0</c>).
    /// </summary>
    private static bool SameAddress(IPAddress hostAddress, IPAddress configured)
    {
        if (hostAddress.AddressFamily != configured.AddressFamily)
            return false;

        if (configured.AddressFamily != AddressFamily.InterNetworkV6 || configured.ScopeId == 0)
            return hostAddress
                .GetAddressBytes()
                .AsSpan()
                .SequenceEqual(configured.GetAddressBytes());

        return hostAddress.Equals(configured);
    }
}
