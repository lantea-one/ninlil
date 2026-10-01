using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Wire.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class is the Router Advertisement transport: a raw ICMPv6 (<c>SOCK_RAW</c>,
///     <c>IPPROTO_ICMPV6</c>) socket that joins <see cref="RaMulticastGroups.AllRouters" /> on
///     every usable interface so Router Solicitations reach it, sends periodic unsolicited Router
///     Advertisements on each interface at the randomized interval of RFC 4861 Section 6.2.4,
///     and answers solicitations after the random delay of Section 6.2.6. The content of every
///     advertisement comes from <see cref="IRaAdvertisementProvider" />
///     (<c>Ninlil.Dhcp.Server.Ra.RaAdvertisementProvider</c>).
///     <para>
///         <b>It needs <c>CAP_NET_RAW</c> (or root).</b> Unlike the UDP listeners, a raw socket
///         has no port that tests could move to an unprivileged number.
///         <c>Icmpv6RaListenerLiveServiceControlTests</c> runs a start, solicit, advertise, stop,
///         restart cycle when the process can open a raw ICMPv6 socket, and otherwise returns
///         early and reports a pass, as <c>Ninlil.Dns.Protocol.DnsOverQuic.Tests.DnsOverQuicListenerTests</c>
///         does for QUIC.
///     </para>
///     <para>
///         <b>It is a restartable <see cref="IDhcpProtocolService" /></b>, registered as a
///         singleton with the same lifecycle as the DHCP listeners:
///         <c>Icmpv6RaListenerHostedService</c> starts it at boot when
///         <see cref="RaProtocolOptions.Enable" /> is <see langword="true" />, and
///         <see cref="_lifecycleLock" /> serializes start and stop. The receive loop and the
///         unsolicited-send loop run as one task, so a stop ends both and a start creates both.
///     </para>
///     <para>
///         <b>Hop Limit 255.</b> RFC 4861 Sections 6.1.1 and 6.1.2 require every Neighbor
///         Discovery message to carry Hop Limit 255 and receivers to drop any that do not, which
///         proves the message was not forwarded from another link. The socket's unicast and
///         multicast hop limits are set to 255 when it is created.
///     </para>
///     <para>
///         <b>Solicited replies do not know the arrival interface.</b> That would need
///         <c>IPV6_RECVPKTINFO</c> through <c>ReceiveMessageFromAsync</c>, which failed with
///         <c>ENOPROTOOPT</c> on a raw ICMPv6 socket on Linux. So a solicitation from a unicast
///         source gets a unicast reply, routed by the address's scope ID. A solicitation from the
///         unspecified address (<c>::</c>) must be answered by multicast (RFC 4861 Section 6.2.6),
///         so that reply is sent on every usable interface, accepting duplicates on hosts with
///         several interfaces.
///     </para>
///     <para>
///         <b>Per-interface content.</b> Wherever the interface is known (the periodic loop and
///         the multicast reply), its name is passed to
///         <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />, which applies that
///         interface's <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c> entry, or returns
///         <see langword="null" /> for an excluded interface. The unicast reply has no interface,
///         so it always carries the default content and ignores overrides and exclusions.
///     </para>
/// </summary>
/// <param name="advertisementProvider">
///     The provider asked for the advertisement content before every send.
/// </param>
/// <param name="options">The advertisement interval settings.</param>
/// <param name="logger">The logger this listener records unexpected socket failures to.</param>
public sealed class Icmpv6RaListener(
    IRaAdvertisementProvider advertisementProvider,
    IOptions<RaProtocolOptions> options,
    ILogger<Icmpv6RaListener> logger
) : IDhcpProtocolService
{
    /// <summary>
    ///     RFC 4861 Section 10's "MAX_RA_DELAY_TIME": the longest a router waits before answering
    ///     a solicitation, so replies from several routers on a link do not arrive together.
    /// </summary>
    private static readonly TimeSpan MaximumSolicitedResponseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     RFC 4861 Section 10's "MIN_DELAY_BETWEEN_RAS": the shortest gap between two
    ///     advertisements, solicited or not, so a burst of solicitations cannot cause a flood.
    ///     RFC 4861 applies it per interface; this listener applies one window to all sends,
    ///     since a solicited unicast reply has no known interface.
    ///     <para>
    ///         A send that falls inside the window is dropped (see
    ///         <see cref="SendAdvertisementAsync" />), where RFC 4861 Section 6.2.6 would defer a
    ///         solicited reply to the end of the window. The client's retransmitted solicitation or
    ///         the next periodic advertisement covers a dropped reply. Because the window is
    ///         shared, the periodic loop and the multicast reply, which send on several interfaces
    ///         in a row, reach only the first interface in one pass; the others are skipped.
    ///     </para>
    /// </summary>
    private static readonly TimeSpan MinimumDelayBetweenAdvertisements = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The hop limit every Neighbor Discovery message must carry (see the class summary).
    /// </summary>
    private const int RequiredHopLimit = 255;

    /// <summary>
    ///     This constant contains the receive buffer size. A Router Solicitation is far smaller;
    ///     a longer datagram is truncated, which is harmless since only solicitations are decoded.
    /// </summary>
    private const int MaximumDatagramSize = 1500;

    private static readonly Random Jitter = Random.Shared;

    /// <summary>
    ///     This lock serializes <see cref="StartAsync" /> and <see cref="StopAsync" />, as in
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener._lifecycleLock</c>.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains the raw socket while the listener runs, or <see langword="null" />.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source shared by the receive and unsolicited-send
    ///     loops, which <see cref="StopAsync" /> cancels.
    /// </summary>
    private CancellationTokenSource _executionCts;

    /// <summary>
    ///     This field contains the task running both loops (through
    ///     <see cref="Task.WhenAll(Task[])" />). <see cref="StopAsync" /> awaits it so neither loop
    ///     uses the socket after it is disposed. Detached solicitation replies are not part of it.
    /// </summary>
    private Task _executionTask;

    /// <summary>
    ///     This field holds the time of the last successful send, shared by all interfaces (see
    ///     <see cref="MinimumDelayBetweenAdvertisements" />). <see cref="StartAsync" /> resets it so
    ///     a restart does not inherit the previous run's window.
    /// </summary>
    private DateTime _lastSentAtUtc = DateTime.MinValue;

    /// <summary>
    ///     This property reports whether the socket is open. It is not cleared if a loop ends on
    ///     an unexpected exception.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    bool IDhcpProtocolService.IsRunning => IsListening;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource executionCts;

        lock (_lifecycleLock)
        {
            // Starting a running listener does nothing (see IDhcpProtocolService).
            if (IsListening)
                return Task.CompletedTask;

            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, ProtocolType.IcmpV6);

            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );

                // Every Neighbor Discovery message must carry Hop Limit 255. Ttl sets the IPv6
                // unicast hop limit and MulticastTimeToLive the multicast one.
                socket.Ttl = RequiredHopLimit;
                socket.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.MulticastTimeToLive,
                    RequiredHopLimit
                );

                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                JoinAllRoutersGroupOnEveryUsableInterface(socket);
            }
            catch
            {
                // On failure (usually a permission error from an unprivileged process) leave
                // IsListening false and let the caller see the exception, as
                // IDhcpProtocolService.StartAsync describes. Creating the socket itself is outside
                // this try, so that failure also propagates.
                socket.Dispose();
                throw;
            }

            _lastSentAtUtc = DateTime.MinValue;

            executionCts = new CancellationTokenSource();

            _socket = socket;
            _executionCts = executionCts;
            IsListening = true;

            logger.LogInformation(
                "The Router Advertisement listener started and is now bound to a raw ICMPv6 socket."
            );

            _executionTask = Task.Run(() => RunLoopsAsync(socket, executionCts.Token));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource executionCts;
        Task executionTask;

        lock (_lifecycleLock)
        {
            // Stopping a stopped listener does nothing (see IDhcpProtocolService).
            if (!IsListening)
                return;

            socket = _socket;
            executionCts = _executionCts;
            executionTask = _executionTask;

            _socket = null;
            _executionCts = null;
            _executionTask = null;
            IsListening = false;
        }

        // Cancel the shared token and close the socket. Closing unblocks a pending
        // ReceiveFromAsync and drops the multicast memberships, which belong to the socket.
        await executionCts.CancelAsync();
        socket.Close();

        if (executionTask is not null)
        {
            try
            {
                await executionTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Either the caller's token fired while waiting or the loops ended by
                // cancellation. The socket is already closed, so they can no longer use it.
            }
        }

        executionCts.Dispose();
        socket.Dispose();

        logger.LogInformation(
            "The Router Advertisement listener stopped and released its bound socket."
        );
    }

    /// <summary>
    ///     This method runs the receive loop and the unsolicited-send loop of one start/stop cycle
    ///     and returns when both have stopped. The socket and token are passed in, as in
    ///     <c>Dhcpv6UdpListener.RunReceiveLoopAsync</c>, so a later cycle's fields never leak in.
    ///     Only cancellation is caught; any other exception ends both loops.
    /// </summary>
    private async Task RunLoopsAsync(Socket socket, CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(
                ReceiveLoopAsync(socket, stoppingToken),
                UnsolicitedSendLoopAsync(socket, stoppingToken)
            );
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    /// <summary>
    ///     This method joins <see cref="RaMulticastGroups.AllRouters" /> on every usable interface
    ///     (see <see cref="EnumerateUsableInterfaces" />), logging and skipping an interface that
    ///     refuses, as
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener.JoinMulticastGroupOnEveryUsableInterface</c>
    ///     does.
    /// </summary>
    private void JoinAllRoutersGroupOnEveryUsableInterface(Socket socket)
    {
        foreach ((int interfaceIndex, string _) in EnumerateUsableInterfaces())
        {
            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.AddMembership,
                    new IPv6MulticastOption(RaMulticastGroups.AllRouters, interfaceIndex)
                );
            }
            catch (SocketException exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not join the Router Advertisement all-routers multicast group on interface index {InterfaceIndex}.",
                    interfaceIndex
                );
            }
        }
    }

    /// <summary>
    ///     This method lists the index and name of every interface that is up, supports IPv6 and
    ///     is not loopback. The multicast join, the periodic loop and the multicast reply all use
    ///     it, and it is re-read each time, so interfaces that appear later get advertisements
    ///     (though not the multicast membership until the next start). The name selects the
    ///     interface's <c>RaInterfaceOverrideOptions</c> entry in
    ///     <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />.
    /// </summary>
    private static IEnumerable<(int Index, string Name)> EnumerateUsableInterfaces()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (
                networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || !networkInterface.Supports(NetworkInterfaceComponent.IPv6)
            )
                continue;

            int interfaceIndex;

            try
            {
                interfaceIndex = networkInterface.GetIPProperties().GetIPv6Properties().Index;
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            yield return (interfaceIndex, networkInterface.Name);
        }
    }

    /// <summary>
    ///     This method runs the periodic unsolicited loop (RFC 4861 Section 6.2.4). Once a second
    ///     it checks every usable interface and, when that interface is due, sends an advertisement
    ///     to <see cref="RaMulticastGroups.AllNodes" /> and schedules the next one a random time
    ///     between <see cref="RaProtocolOptions.MinRouterAdvertisementInterval" /> and
    ///     <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" /> later. The randomness
    ///     keeps routers on a link from synchronizing. The next time is scheduled even when the
    ///     send was skipped by <see cref="MinimumDelayBetweenAdvertisements" />.
    /// </summary>
    private async Task UnsolicitedSendLoopAsync(Socket socket, CancellationToken stoppingToken)
    {
        Dictionary<int, DateTime> nextSendAtUtcByInterfaceIndex = [];

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime now = DateTime.UtcNow;

            foreach ((int interfaceIndex, string interfaceName) in EnumerateUsableInterfaces())
            {
                if (
                    nextSendAtUtcByInterfaceIndex.TryGetValue(
                        interfaceIndex,
                        out DateTime nextSendAtUtc
                    )
                    && nextSendAtUtc > now
                )
                    continue;

                await SendAdvertisementAsync(
                    socket,
                    interfaceIndex,
                    interfaceName,
                    RaMulticastGroups.AllNodes,
                    stoppingToken
                );
                nextSendAtUtcByInterfaceIndex[interfaceIndex] = now.Add(NextUnsolicitedInterval());
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method picks the next unsolicited interval uniformly between the configured
    ///     minimum and maximum (RFC 4861 Section 6.2.4), or the maximum when the minimum is not
    ///     smaller.
    /// </summary>
    private TimeSpan NextUnsolicitedInterval()
    {
        double minSeconds = options.Value.MinRouterAdvertisementInterval.TotalSeconds;
        double maxSeconds = options.Value.MaxRouterAdvertisementInterval.TotalSeconds;

        if (maxSeconds <= minSeconds)
            return TimeSpan.FromSeconds(maxSeconds);

        double seconds = minSeconds + Jitter.NextDouble() * (maxSeconds - minSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    ///     This method runs the receive loop. A raw ICMPv6 socket receives every ICMPv6 message on
    ///     the host, so anything that is not a valid Router Solicitation is skipped without
    ///     logging. Each solicitation is answered by <see cref="HandleSolicitationAsync" />, started
    ///     without awaiting. The IPv6 hop limit of a solicitation is not checked.
    /// </summary>
    private async Task ReceiveLoopAsync(Socket socket, CancellationToken stoppingToken)
    {
        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await socket.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.IPv6Any, 0),
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                logger.LogWarning(exception, "Error receiving a Router Advertisement datagram.");
                continue;
            }

            ReadOnlySpan<byte> datagram = receiveBuffer.AsSpan(0, result.ReceivedBytes);

            // Skip every other ICMPv6 message type quietly.
            if (
                datagram.Length == 0
                || datagram[0] != (byte)Common.Enum.RaMessageTypeEnum.RouterSolicitation
            )
                continue;

            if (!RaMessageReader.TryReadRouterSolicitation(datagram, out _, out _))
                continue;

            IPAddress solicitationSource = ((IPEndPoint)result.RemoteEndPoint).Address;

            _ = HandleSolicitationAsync(socket, solicitationSource, stoppingToken);
        }
    }

    /// <summary>
    ///     This method answers one Router Solicitation after RFC 4861 Section 6.2.6's random delay
    ///     (0 to <see cref="MaximumSolicitedResponseDelay" />): by unicast to the sender when it has
    ///     an address (the scope ID routes the reply), or by multicast to
    ///     <see cref="RaMulticastGroups.AllNodes" /> on every usable interface when it came from
    ///     <c>::</c> (see the class summary). <see cref="ReceiveLoopAsync" /> does not await it,
    ///     so the delay does not hold up the next receive.
    /// </summary>
    private async Task HandleSolicitationAsync(
        Socket socket,
        IPAddress solicitationSource,
        CancellationToken stoppingToken
    )
    {
        try
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(
                Jitter.NextDouble() * MaximumSolicitedResponseDelay.TotalMilliseconds
            );
            await Task.Delay(delay, stoppingToken);

            if (!solicitationSource.Equals(IPAddress.IPv6None))
            {
                // The arrival interface is unknown (see the class summary), so the reply uses the
                // default content and ignores per-interface overrides and exclusions.
                await SendAdvertisementAsync(
                    socket,
                    interfaceIndex: null,
                    interfaceName: null,
                    solicitationSource,
                    stoppingToken
                );
                return;
            }

            // RFC 4861 Section 6.2.6: a solicitation from the unspecified address is answered by
            // multicast. The arrival interface is unknown, so send on every usable interface,
            // each with its own content (or nothing, for an excluded interface).
            foreach ((int interfaceIndex, string interfaceName) in EnumerateUsableInterfaces())
                await SendAdvertisementAsync(
                    socket,
                    interfaceIndex,
                    interfaceName,
                    RaMulticastGroups.AllNodes,
                    stoppingToken
                );
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    /// <summary>
    ///     This method asks <see cref="IRaAdvertisementProvider" /> for the advertisement and sends
    ///     it, unless the last send was less than <see cref="MinimumDelayBetweenAdvertisements" />
    ///     ago or the provider returns <see langword="null" />. Building and encoding the
    ///     advertisement happen outside the try/catch, so an exception there propagates to the
    ///     calling loop.
    /// </summary>
    /// <param name="socket">The socket to send on.</param>
    /// <param name="interfaceIndex">
    ///     The interface to use as the outgoing multicast interface, or <see langword="null" />
    ///     for a unicast send, which the destination's scope ID routes.
    /// </param>
    /// <param name="interfaceName">
    ///     The name of that interface, used to select its content override, or
    ///     <see langword="null" /> when the interface is unknown, as for a unicast reply (see
    ///     <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />).
    /// </param>
    /// <param name="destination">Where to send the advertisement.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    private async Task SendAdvertisementAsync(
        Socket socket,
        int? interfaceIndex,
        string interfaceName,
        IPAddress destination,
        CancellationToken stoppingToken
    )
    {
        DateTime now = DateTime.UtcNow;

        if (now - _lastSentAtUtc < MinimumDelayBetweenAdvertisements)
            return;

        RouterAdvertisementMessage advertisement = advertisementProvider.BuildCurrentAdvertisement(
            interfaceName
        );

        if (advertisement is null)
            return;

        byte[] bytes = RaMessageWriter.WriteRouterAdvertisement(advertisement);

        try
        {
            if (interfaceIndex is { } index)
                socket.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.MulticastInterface,
                    index
                );

            await socket.SendToAsync(bytes, new IPEndPoint(destination, 0), stoppingToken);
            _lastSentAtUtc = now;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a Router Advertisement to {Destination}.",
                destination
            );
        }
    }
}
