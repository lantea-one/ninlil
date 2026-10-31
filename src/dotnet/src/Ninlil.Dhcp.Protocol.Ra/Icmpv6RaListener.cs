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
///         Discovery message to carry Hop Limit 255. The socket's unicast and multicast hop
///         limits are set to 255 when it is created, so every advertisement it sends complies.
///     </para>
///     <para>
///         <b>Solicitations are checked by source address, not hop limit.</b> Section 6.1.1
///         tells a router to drop a solicitation whose hop limit is not 255, which proves the
///         packet was not forwarded from another link. .NET cannot read the hop limit of a
///         received packet on a raw ICMPv6 socket (it has no hop-limit control message, and
///         <c>IPV6_RECVPKTINFO</c> already fails there), and reading it would need native
///         <c>recvmsg</c> interop that differs per operating system. This listener instead
///         accepts a solicitation only from a link-local (<c>fe80::/10</c>) or unspecified
///         (<c>::</c>) source (see <see cref="IsAcceptableSolicitation" />). A router never
///         forwards a packet with a link-local source (RFC 4291 Section 2.5.6), so such a
///         packet came from the local link, the guarantee the hop-limit check exists to give.
///         The loopback address <c>::1</c> is accepted too, since a packet from it cannot
///         leave its host (RFC 4291 Section 2.5.3). This is a deviation from Section 6.1.1: a
///         host on the link can forge a link-local source, exactly as it could forge a hop
///         limit of 255. As in the RFC, a solicitation from <c>::</c> must not carry a Source
///         Link-Layer Address option.
///     </para>
///     <para>
///         <b>Solicited replies do not know the arrival interface.</b> That would need
///         <c>IPV6_RECVPKTINFO</c> through <c>ReceiveMessageFromAsync</c>, which failed with
///         <c>ENOPROTOOPT</c> on a raw ICMPv6 socket on Linux. So a solicitation from a unicast
///         source gets a unicast reply, routed by the address's scope ID. A solicitation from the
///         unspecified address (<c>::</c>) can only be answered by multicast (RFC 4861 Section 6.2.6 allows a unicast answer to a
///         source other than <c>::</c>),
///         so that reply is sent on every usable interface, accepting duplicates on hosts with
///         several interfaces.
///     </para>
///     <para>
///         <b>Rate limits.</b> <see cref="RaSendWindow" /> keeps RFC 4861 Section 6.2.6's
///         MIN_DELAY_BETWEEN_RAS window per interface for multicast advertisements only. The
///         periodic loop sends to every due interface and retries a refused one when its window
///         ends. A solicited multicast reply that falls inside a window waits for the end of it
///         instead of being dropped. A solicited unicast reply has its own bounded window per
///         destination, which the RFC does not require (see <see cref="RaSendWindow" />).
///         Unicast and multicast never share a window.
///     </para>
///     <para>
///         <b>Size.</b> An advertisement is at most <see cref="MaximumAdvertisementBytes" />
///         octets, so it is never fragmented on a link with the IPv6 minimum MTU. A larger one is
///         sent as several advertisements that each repeat the header fields and the Source
///         Link-Layer Address and MTU options (see <see cref="TryBuildAdvertisements" />). The
///         parts of one advertisement are one send for the rate-limit window.
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
/// <param name="timeProvider">
///     The clock for every delay, interval and window; <see langword="null" /> (the default, which
///     dependency injection uses when none is registered) means <see cref="TimeProvider.System" />.
///     Tests pass a manual clock.
/// </param>
public sealed class Icmpv6RaListener(
    IRaAdvertisementProvider advertisementProvider,
    IOptions<RaProtocolOptions> options,
    ILogger<Icmpv6RaListener> logger,
    TimeProvider timeProvider = null
) : IDhcpProtocolService
{
    /// <summary>
    ///     RFC 4861 Section 10's "MAX_RA_DELAY_TIME": the longest a router waits before answering
    ///     a solicitation, so replies from several routers on a link do not arrive together.
    /// </summary>
    private static readonly TimeSpan MaximumSolicitedResponseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     The hop limit every Neighbor Discovery message must carry (see the class summary).
    /// </summary>
    private const int RequiredHopLimit = 255;

    /// <summary>
    ///     This constant contains the most solicited replies that may wait for their random delay
    ///     or their send at one time. A solicitation over the limit is dropped, since the client
    ///     retransmits it and a flood of solicitations would otherwise start one task each.
    /// </summary>
    internal const int MaxPendingSolicitedReplies = 16;

    /// <summary>
    ///     This constant contains the receive buffer size. A Router Solicitation is far smaller;
    ///     a longer datagram is truncated, which is harmless since only solicitations are decoded.
    /// </summary>
    private const int MaximumDatagramSize = 1500;

    /// <summary>
    ///     This constant contains the largest size in octets of one advertisement datagram, the
    ///     ICMPv6 message without the IPv6 header: the IPv6 minimum link MTU of 1280 minus 40
    ///     (RFC 8200 Section 5). A larger message may be fragmented on a link with the minimum MTU,
    ///     and hosts drop fragmented Neighbor Discovery messages (RFC 6980 Section 5), so
    ///     <see cref="TryBuildAdvertisements" /> splits a larger message into several datagrams.
    /// </summary>
    internal const int MaximumAdvertisementBytes = 1240;

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
    ///     This field contains the clock behind every delay and window (see the constructor
    ///     parameter).
    /// </summary>
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field contains the send windows (see <see cref="RaSendWindow" />).
    ///     <see cref="StartAsync" /> resets them, so a restart does not inherit the previous run's
    ///     windows.
    /// </summary>
    private readonly RaSendWindow _sendWindow = new(timeProvider ?? TimeProvider.System);

    /// <summary>
    ///     This property contains the function that lists the usable interfaces. It defaults to
    ///     <see cref="EnumerateUsableInterfaces" />; tests replace it with a fixed list.
    /// </summary>
    internal Func<IEnumerable<(int Index, string Name)>> InterfaceEnumerator { get; set; } =
        EnumerateUsableInterfaces;

    /// <summary>
    ///     This property contains the function that wraps the raw socket in the sender the loops
    ///     use. It defaults to <see cref="SocketRaDatagramSender" />. Tests do not open the
    ///     socket; they pass a recording sender straight to the loop methods.
    /// </summary>
    internal Func<Socket, IRaDatagramSender> SenderFactory { get; set; } =
        socket => new SocketRaDatagramSender(socket);

    /// <summary>
    ///     This property exposes the send windows to tests.
    /// </summary>
    internal RaSendWindow SendWindow => _sendWindow;

    /// <summary>
    ///     This field counts the detached solicited replies that have not finished, which
    ///     <see cref="MaxPendingSolicitedReplies" /> limits. It is only changed with
    ///     <see cref="Interlocked" />.
    /// </summary>
    private int _pendingSolicitedReplies;

    /// <summary>
    ///     This field is 1 while a multicast reply to a solicitation from <c>::</c> is pending.
    ///     Another such solicitation then adds nothing, because one multicast advertisement
    ///     answers all of them (RFC 4861 Section 6.2.6). It is only changed with
    ///     <see cref="Interlocked" />.
    /// </summary>
    private int _multicastReplyPending;

    /// <summary>
    ///     This lock guards <see cref="_lastBuildFailureMessage" />.
    /// </summary>
    private readonly Lock _buildFailureLock = new();

    /// <summary>
    ///     This field contains the message of the exception that made the last advertisement
    ///     build fail, or <see langword="null" /> after a build succeeds. It keeps a persistent
    ///     failure from logging an Error every few seconds (see
    ///     <see cref="TryBuildAdvertisements" />).
    /// </summary>
    private string _lastBuildFailureMessage;

    /// <summary>
    ///     This property contains why the last Router Advertisement start failed to bind its socket (the
    ///     operating system's message, for example "Address already in use"), or
    ///     <see langword="null" /> when the last start worked or none has run. The
    ///     <c>dhcpServices</c> health check shows it, because the boot-time start logs the failure
    ///     and goes on instead of stopping the host.
    /// </summary>
    public string StartFailure { get; private set; }

    /// <summary>
    ///     This property reports whether the socket is open. It stays true if a loop ends
    ///     unexpectedly, so <see cref="StopAsync" /> can still release the socket; use
    ///     <see cref="IDhcpProtocolService.IsRunning" /> to learn whether the loops are running.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property reports whether the socket is open and the loops are still running, so
    ///     a listener whose loops ended is not reported as healthy.
    /// </summary>
    bool IDhcpProtocolService.IsRunning
    {
        get
        {
            lock (_lifecycleLock)
                return IsListening && _executionTask is { IsCompleted: false };
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource executionCts;

        lock (_lifecycleLock)
        {
            // Starting a running listener does nothing (see IDhcpProtocolService).
            if (IsListening)
            {
                if (_executionTask is { IsCompleted: false })
                    return Task.CompletedTask;

                // The loops ended on their own, so the listener is bound but deaf. Release the
                // old socket and bind again.
                logger.LogWarning(
                    "The Router Advertisement loops are not running; releasing the old socket and starting again."
                );

                _socket.Dispose();
                _executionCts.Dispose();

                _socket = null;
                _executionCts = null;
                _executionTask = null;
                IsListening = false;
            }

            socket = null;

            try
            {
                socket = new Socket(
                    AddressFamily.InterNetworkV6,
                    SocketType.Raw,
                    ProtocolType.IcmpV6
                );
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
            catch (Exception exception)
            {
                // On failure (usually a permission error from an unprivileged process, which
                // already fails when the raw socket is created) leave IsListening false, keep the
                // reason for the health check and let the caller see the exception, as
                // IDhcpProtocolService.StartAsync describes.
                socket?.Dispose();
                StartFailure = exception.Message;
                throw;
            }

            _sendWindow.Reset();

            executionCts = new CancellationTokenSource();

            _socket = socket;
            _executionCts = executionCts;
            IsListening = true;
            StartFailure = null;

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

        try
        {
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
                catch (Exception exception)
                {
                    // A loop that faulted must not skip the disposals below.
                    logger.LogError(
                        exception,
                        "The Router Advertisement loops ended with an error."
                    );
                }
            }
        }
        finally
        {
            executionCts.Dispose();
            socket.Dispose();
        }

        logger.LogInformation(
            "The Router Advertisement listener stopped and released its bound socket."
        );
    }

    /// <summary>
    ///     This method runs the receive loop and the unsolicited-send loop of one start/stop cycle
    ///     and returns when both have stopped. The socket and token are passed in, as in
    ///     <c>Dhcpv6UdpListener.RunReceiveLoopAsync</c>, so a later cycle's fields never leak in.
    ///     Each loop handles its own failures (one bad advertisement or datagram never ends it).
    ///     If a loop ends while no stop was requested, this method logs a Critical entry, stops
    ///     the other loop and returns, so <see cref="IDhcpProtocolService.IsRunning" /> turns
    ///     false rather than reporting half a listener as healthy.
    /// </summary>
    private async Task RunLoopsAsync(Socket socket, CancellationToken stoppingToken)
    {
        using CancellationTokenSource loopsCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );

        IRaDatagramSender sender = SenderFactory(socket);

        Task receiveLoop = GuardLoopAsync(
            "receive",
            () => ReceiveLoopAsync(socket, sender, loopsCts.Token),
            stoppingToken
        );
        Task sendLoop = GuardLoopAsync(
            "unsolicited-send",
            () => UnsolicitedSendLoopAsync(sender, loopsCts.Token),
            stoppingToken
        );

        // The first loop to end (normally both end together, on stop) takes the other with it.
        await Task.WhenAny(receiveLoop, sendLoop);
        await loopsCts.CancelAsync();
        await Task.WhenAll(receiveLoop, sendLoop);
    }

    /// <summary>
    ///     This method runs one loop and never throws. An exception, or a normal return, while no
    ///     stop was requested is logged as Critical.
    /// </summary>
    private async Task GuardLoopAsync(
        string loopName,
        Func<Task> loop,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await loop();
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogCritical(
                exception,
                "The Router Advertisement {Loop} loop stopped unexpectedly.",
                loopName
            );
            return;
        }
        catch (Exception)
        {
            // An exception during a stop is the stop itself (cancellation or a closed socket).
            return;
        }

        if (!stoppingToken.IsCancellationRequested)
            logger.LogCritical(
                "The Router Advertisement {Loop} loop stopped unexpectedly.",
                loopName
            );
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
        foreach ((int interfaceIndex, string _) in InterfaceEnumerator())
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
    ///     is not loopback. It is the default of <see cref="InterfaceEnumerator" />, which the
    ///     multicast join, the periodic loop and the multicast reply all use, and it is re-read each time, so interfaces that appear later get advertisements
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
    ///     This method runs the periodic unsolicited loop (RFC 4861 Section 6.2.4): once a second
    ///     it calls <see cref="RunUnsolicitedPassAsync" />.
    /// </summary>
    private async Task UnsolicitedSendLoopAsync(
        IRaDatagramSender sender,
        CancellationToken stoppingToken
    )
    {
        Dictionary<int, DateTimeOffset> nextSendAtByInterfaceIndex = [];

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunUnsolicitedPassAsync(sender, nextSendAtByInterfaceIndex, stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method checks every usable interface once and, for each one that is due, sends an
    ///     advertisement to <see cref="RaMulticastGroups.AllNodes" /> and schedules the next one a
    ///     random time between <see cref="RaProtocolOptions.MinRouterAdvertisementInterval" /> and
    ///     <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" /> later. The randomness
    ///     keeps routers on a link from synchronizing.
    ///     <para>
    ///         Each interface has its own multicast window (<see cref="RaSendWindow" />), so one
    ///         pass reaches every due interface. An interface whose window is still open is not
    ///         rescheduled by a full interval: it is retried when the window ends. A failed send
    ///         still schedules the next time, so a failing interface never ends the loop.
    ///     </para>
    /// </summary>
    /// <param name="sender">Where the advertisements go.</param>
    /// <param name="nextSendAtByInterfaceIndex">
    ///     The time each interface is next due; the method reads and updates it.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    internal async Task RunUnsolicitedPassAsync(
        IRaDatagramSender sender,
        Dictionary<int, DateTimeOffset> nextSendAtByInterfaceIndex,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = _time.GetUtcNow();

        List<(int Index, string Name)> interfaces;

        try
        {
            interfaces = InterfaceEnumerator().ToList();
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Listing the network interfaces failed.");
            interfaces = [];
        }

        foreach ((int interfaceIndex, string interfaceName) in interfaces)
        {
            if (
                nextSendAtByInterfaceIndex.TryGetValue(
                    interfaceIndex,
                    out DateTimeOffset nextSendAt
                )
                && nextSendAt > now
            )
                continue;

            // An open window on this interface only delays this interface (RFC 4861 Section 6.2.6).
            if (!_sendWindow.TryReserveMulticastNow(interfaceIndex, out TimeSpan retryAfter))
            {
                nextSendAtByInterfaceIndex[interfaceIndex] = now + retryAfter;
                continue;
            }

            try
            {
                await SendAdvertisementAsync(
                    sender,
                    interfaceIndex,
                    interfaceName,
                    RaMulticastGroups.AllNodes,
                    stoppingToken
                );
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Sending the periodic Router Advertisement on interface {InterfaceName} failed.",
                    interfaceName
                );
            }

            nextSendAtByInterfaceIndex[interfaceIndex] = now + NextUnsolicitedInterval();
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
    ///     logging. A solicitation that fails <see cref="IsAcceptableSolicitation" />, or that
    ///     arrives while too many replies are pending, is dropped with a Debug entry. Each other
    ///     solicitation is answered by <see cref="HandleSolicitationAsync" />, started without
    ///     awaiting.
    /// </summary>
    private async Task ReceiveLoopAsync(
        Socket socket,
        IRaDatagramSender sender,
        CancellationToken stoppingToken
    )
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

            if (
                !RaMessageReader.TryReadRouterSolicitation(
                    datagram,
                    out RouterSolicitationMessage solicitation,
                    out _
                )
            )
                continue;

            IPAddress solicitationSource = ((IPEndPoint)result.RemoteEndPoint).Address;

            if (!IsAcceptableSolicitation(solicitationSource, solicitation))
            {
                logger.LogDebug(
                    "Dropped a Router Solicitation from {Source}: it is not from the local link.",
                    solicitationSource
                );
                continue;
            }

            bool isMulticastReply = solicitationSource.Equals(IPAddress.IPv6Any);

            // One pending multicast reply answers every solicitation from "::" that arrives
            // before it is sent (RFC 4861 Section 6.2.6).
            if (isMulticastReply && Interlocked.Exchange(ref _multicastReplyPending, 1) == 1)
                continue;

            if (Interlocked.Increment(ref _pendingSolicitedReplies) > MaxPendingSolicitedReplies)
            {
                Interlocked.Decrement(ref _pendingSolicitedReplies);

                if (isMulticastReply)
                    Volatile.Write(ref _multicastReplyPending, 0);

                logger.LogDebug(
                    "Dropped a Router Solicitation from {Source}: too many replies are pending.",
                    solicitationSource
                );
                continue;
            }

            _ = HandleSolicitationAsync(sender, solicitationSource, stoppingToken);
        }
    }

    /// <summary>
    ///     This method runs <see cref="AnswerSolicitationAsync" /> for one accepted solicitation.
    ///     <see cref="ReceiveLoopAsync" /> does not await it, so the delay does not hold up the
    ///     next receive. It runs detached, so it catches every exception, and it releases the
    ///     pending-reply slot the receive loop took.
    /// </summary>
    private async Task HandleSolicitationAsync(
        IRaDatagramSender sender,
        IPAddress solicitationSource,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await AnswerSolicitationAsync(sender, solicitationSource, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on stop.
        }
        catch (Exception exception)
        {
            // Nothing observes this task, so an exception must end here.
            logger.LogError(
                exception,
                "Answering a Router Solicitation from {Source} failed.",
                solicitationSource
            );
        }
        finally
        {
            if (solicitationSource.Equals(IPAddress.IPv6Any))
                Volatile.Write(ref _multicastReplyPending, 0);

            Interlocked.Decrement(ref _pendingSolicitedReplies);
        }
    }

    /// <summary>
    ///     This method answers one Router Solicitation after RFC 4861 Section 6.2.6's random delay
    ///     (0 to <see cref="MaximumSolicitedResponseDelay" />): by unicast to the sender when it has
    ///     an address (the scope ID routes the reply), or by multicast to
    ///     <see cref="RaMulticastGroups.AllNodes" /> on every usable interface when it came from
    ///     <c>::</c> (see the class summary).
    ///     <para>
    ///         A unicast reply takes the destination's window (<see cref="RaSendWindow" />); a
    ///         refused one is dropped with a Debug entry, since the client retransmits. A multicast
    ///         reply runs on every interface at once, and each interface waits for its own next
    ///         free multicast slot. A reply inside a window is therefore sent when the window ends
    ///         (RFC 4861 Section 6.2.6), and a busy interface never holds up another.
    ///     </para>
    /// </summary>
    /// <param name="sender">Where the advertisements go.</param>
    /// <param name="solicitationSource">The source address of the solicitation.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    internal async Task AnswerSolicitationAsync(
        IRaDatagramSender sender,
        IPAddress solicitationSource,
        CancellationToken stoppingToken
    )
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(
            Jitter.NextDouble() * MaximumSolicitedResponseDelay.TotalMilliseconds
        );
        await Task.Delay(delay, _time, stoppingToken);

        // "::" is IPAddress.IPv6Any and IPAddress.IPv6None alike; IPv6Any matches the receive loop.
        if (!solicitationSource.Equals(IPAddress.IPv6Any))
        {
            if (!_sendWindow.TryReserveUnicast(solicitationSource))
            {
                logger.LogDebug(
                    "Dropped the reply to a Router Solicitation from {Source}: it was answered within the last few seconds.",
                    solicitationSource
                );
                return;
            }

            // The arrival interface is unknown (see the class summary), so the reply uses the
            // default content and ignores per-interface overrides and exclusions.
            await SendAdvertisementAsync(
                sender,
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
        List<Task> replies = [];

        foreach ((int interfaceIndex, string interfaceName) in InterfaceEnumerator())
            replies.Add(
                SendMulticastReplyAsync(sender, interfaceIndex, interfaceName, stoppingToken)
            );

        await Task.WhenAll(replies);
    }

    /// <summary>
    ///     This method sends the multicast reply on one interface at that interface's next free
    ///     slot (see <see cref="RaSendWindow.ReserveNextMulticastSlot" />). The slot is reserved
    ///     before the wait, so replies queued on the same interface stay one window apart.
    /// </summary>
    private async Task SendMulticastReplyAsync(
        IRaDatagramSender sender,
        int interfaceIndex,
        string interfaceName,
        CancellationToken stoppingToken
    )
    {
        TimeSpan wait = _sendWindow.ReserveNextMulticastSlot(interfaceIndex);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, _time, stoppingToken);

        await SendAdvertisementAsync(
            sender,
            interfaceIndex,
            interfaceName,
            RaMulticastGroups.AllNodes,
            stoppingToken
        );
    }

    /// <summary>
    ///     This method decides whether a Router Solicitation is accepted. It must come from a
    ///     link-local unicast address (<c>fe80::/10</c>), the loopback address, or the
    ///     unspecified address <c>::</c>; with <c>::</c> it must not carry a Source Link-Layer
    ///     Address option (RFC 4861 Section 6.1.1). This replaces the hop-limit check of that
    ///     section (see the class summary).
    /// </summary>
    /// <param name="source">The source address of the datagram.</param>
    /// <param name="solicitation">The decoded solicitation.</param>
    internal static bool IsAcceptableSolicitation(
        IPAddress source,
        RouterSolicitationMessage solicitation
    )
    {
        if (source.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (source.Equals(IPAddress.IPv6Any))
            return !solicitation.Options.Any(option =>
                option.Type == (byte)Common.Enum.RaOptionCodeEnum.SourceLinkLayerAddress
            );

        return source.IsIPv6LinkLocal || IPAddress.IsLoopback(source);
    }

    /// <summary>
    ///     This method builds and encodes the advertisement for one interface, as one datagram or,
    ///     when the encoded message is larger than <see cref="MaximumAdvertisementBytes" />, as
    ///     several (see <see cref="SplitAdvertisement" />). It returns <see langword="false" />
    ///     with no datagrams when the provider returns <see langword="null" /> (an excluded
    ///     interface), and also when the provider or the encoder throws: the exception is logged
    ///     and never propagates, because a bad configuration must not end a loop. A failure logs
    ///     an Error the first time; the same failure again logs only at Debug until a build
    ///     succeeds, so one bad setting is reported once, not every few seconds. An option that
    ///     had to be dropped is such a failure too, although the build then still succeeds.
    /// </summary>
    /// <param name="interfaceName">
    ///     The interface to build for, or <see langword="null" /> for the default content.
    /// </param>
    /// <param name="datagrams">
    ///     The encoded advertisement datagrams, in send order, when this method returns true. A
    ///     message within the limit gives one datagram, identical to the output of
    ///     <see cref="RaMessageWriter.WriteRouterAdvertisement" />.
    /// </param>
    internal bool TryBuildAdvertisements(string interfaceName, out IReadOnlyList<byte[]> datagrams)
    {
        datagrams = null;
        string droppedOptions = null;

        try
        {
            RouterAdvertisementMessage advertisement =
                advertisementProvider.BuildCurrentAdvertisement(interfaceName);

            if (advertisement is null)
                return false;

            byte[] whole = RaMessageWriter.WriteRouterAdvertisement(advertisement);

            datagrams =
                whole.Length <= MaximumAdvertisementBytes
                    ? [whole]
                    : SplitAdvertisement(advertisement, out droppedOptions);
        }
        catch (Exception exception)
        {
            ReportBuildProblem(interfaceName, exception.Message, exception);
            datagrams = null;
            return false;
        }

        if (droppedOptions is not null)
            ReportBuildProblem(interfaceName, droppedOptions, exception: null);
        else
            lock (_buildFailureLock)
                _lastBuildFailureMessage = null;

        return true;
    }

    /// <summary>
    ///     This method logs a build problem: an Error the first time its message occurs since the
    ///     last clean build, and a Debug entry when the same message occurs again.
    /// </summary>
    private void ReportBuildProblem(string interfaceName, string message, Exception exception)
    {
        bool isRepeat;

        lock (_buildFailureLock)
        {
            isRepeat = _lastBuildFailureMessage == message;
            _lastBuildFailureMessage = message;
        }

        if (isRepeat)
            logger.LogDebug(
                exception,
                "Building the Router Advertisement for interface {InterfaceName} failed again: {Problem}",
                interfaceName,
                message
            );
        else
            logger.LogError(
                exception,
                "Building the Router Advertisement for interface {InterfaceName} failed; the advertisement is not complete or is not sent until the configuration is fixed: {Problem}",
                interfaceName,
                message
            );
    }

    /// <summary>
    ///     This method splits an advertisement that is larger than
    ///     <see cref="MaximumAdvertisementBytes" /> into several messages that each fit. RFC 4861
    ///     Section 6.2.3 lets a router describe a link in several advertisements, each carrying
    ///     a subset of the options. Every part repeats the header fields and the Source
    ///     Link-Layer Address and MTU options, so a host that sees only one part still has the
    ///     link's basics. The other options are packed in their original order: a new part starts
    ///     when the next option does not fit after the ones before it, so the order of the
    ///     prefixes and routes is kept and no option appears twice. An option that does not fit
    ///     even next to the repeated ones is dropped and named in <paramref name="dropped" />.
    /// </summary>
    /// <param name="advertisement">The message whose encoding is too large.</param>
    /// <param name="dropped">
    ///     A description of the dropped options, or <see langword="null" /> when none was dropped.
    /// </param>
    /// <returns>The encoded parts, in send order. There is always at least one.</returns>
    private static List<byte[]> SplitAdvertisement(
        RouterAdvertisementMessage advertisement,
        out string dropped
    )
    {
        // The ICMPv6 header and the Router Advertisement fixed fields.
        const int headerLength =
            RaConstants.Icmpv6HeaderLength + RaConstants.RouterAdvertisementFixedBodyLength;

        // Selected by type, not with Except: that is a set operation and would merge two
        // identical options.
        static bool IsRepeated(RaOption option) =>
            option.Type
                is (byte)Common.Enum.RaOptionCodeEnum.SourceLinkLayerAddress
                    or (byte)Common.Enum.RaOptionCodeEnum.Mtu;

        List<RaOption> repeated = [.. advertisement.Options.Where(IsRepeated)];
        int repeatedLength = repeated.Sum(OptionLength);
        List<List<RaOption>> parts = [];
        List<string> droppedTypes = [];
        int currentLength = headerLength + repeatedLength;

        foreach (
            RaOption option in advertisement.Options.Where(candidate => !IsRepeated(candidate))
        )
        {
            int length = OptionLength(option);

            if (headerLength + repeatedLength + length > MaximumAdvertisementBytes)
            {
                droppedTypes.Add($"type {option.Type} ({length} octets)");
                continue;
            }

            if (parts.Count == 0 || currentLength + length > MaximumAdvertisementBytes)
            {
                parts.Add([.. repeated]);
                currentLength = headerLength + repeatedLength;
            }

            parts[^1].Add(option);
            currentLength += length;
        }

        if (parts.Count == 0)
            parts.Add([.. repeated]);

        dropped =
            droppedTypes.Count == 0
                ? null
                : "A Router Advertisement option is larger than one datagram allows and is dropped: "
                    + string.Join(", ", droppedTypes)
                    + ".";

        return
        [
            .. parts.Select(options =>
            {
                RouterAdvertisementMessage part = new()
                {
                    CurrentHopLimit = advertisement.CurrentHopLimit,
                    ManagedAddressConfiguration = advertisement.ManagedAddressConfiguration,
                    OtherConfiguration = advertisement.OtherConfiguration,
                    RouterLifetime = advertisement.RouterLifetime,
                    ReachableTime = advertisement.ReachableTime,
                    RetransTimer = advertisement.RetransTimer,
                };
                part.Options.AddRange(options);
                return RaMessageWriter.WriteRouterAdvertisement(part);
            }),
        ];
    }

    /// <summary>
    ///     This method returns the encoded size of an option: its 2-octet header and its value.
    /// </summary>
    private static int OptionLength(RaOption option) =>
        RaConstants.OptionHeaderLength + option.Data.Length;

    /// <summary>
    ///     This method builds the advertisement (see <see cref="TryBuildAdvertisements" />) and
    ///     sends its datagrams back to back. The caller has already taken the send window (see
    ///     <see cref="RaSendWindow" />) once for the whole advertisement, so the parts of a split
    ///     advertisement do not take it again. A build failure or a send error is logged and
    ///     reported as <see langword="false" />; a send error stops the remaining parts.
    /// </summary>
    /// <param name="sender">Where the advertisement goes.</param>
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
    /// <returns>Whether every datagram of the advertisement was sent.</returns>
    private async Task<bool> SendAdvertisementAsync(
        IRaDatagramSender sender,
        int? interfaceIndex,
        string interfaceName,
        IPAddress destination,
        CancellationToken stoppingToken
    )
    {
        if (!TryBuildAdvertisements(interfaceName, out IReadOnlyList<byte[]> datagrams))
            return false;

        try
        {
            foreach (byte[] datagram in datagrams)
                await sender.SendAsync(datagram, destination, interfaceIndex, stoppingToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a Router Advertisement to {Destination}.",
                destination
            );
            return false;
        }
    }
}
