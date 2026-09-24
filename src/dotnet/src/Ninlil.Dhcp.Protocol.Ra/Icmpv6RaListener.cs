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
///     This class implements the Router Advertisement transport itself: a raw ICMPv6
///     (<c>SOCK_RAW</c>/<c>IPPROTO_ICMPV6</c>) socket that joins
///     <see cref="RaMulticastGroups.AllRouters" /> on every usable interface (so a genuine Router
///     Solicitation is actually delivered to it), sends a periodic unsolicited Router
///     Advertisement on each such interface per RFC 4861 Section 6.2.4's own randomized-interval
///     rule, and replies to a real solicitation after Section 6.2.6's own randomized response
///     delay -- asking <see cref="IRaAdvertisementProvider" /> what to actually send each time. It
///     does nothing beyond that: every real content decision belongs to whichever provider
///     dependency injection resolves.
///     <para>
///         <b>This transport has no unprivileged equivalent at all, unlike every other transport
///         in this project</b>: a <c>SOCK_RAW</c> socket needs <c>CAP_NET_RAW</c> (or root)
///         regardless of which "port" it might otherwise bind -- ICMPv6 has no port concept for a
///         raw socket to rebind onto a high, unprivileged number the way DHCPv4/DHCPv6's own UDP
///         listeners already do for their own committed round-trip tests. A dedicated, committed
///         xUnit test (<c>Icmpv6RaListenerLiveServiceControlTests</c>) still performs a genuine
///         start -&gt; solicit -&gt; real advertisement -&gt; stop -&gt; confirm-silent -&gt;
///         restart -&gt; real advertisement-again round trip, but only when the current process
///         actually has permission to open a raw ICMPv6 socket (checked once, via a real attempted
///         socket creation, at the top of that test) -- on a typical unprivileged CI runner
///         lacking <c>CAP_NET_RAW</c>, that same test exits early, reported as passed rather than
///         skipped, mirroring <c>Ninlil.Dns.Protocol.DnsOverQuic.Tests.DnsOverQuicListenerTests</c>'
///         own established convention for an environment-dependent capability this repository has
///         no control over. Root privileges (or an explicit <c>CAP_NET_RAW</c> grant) genuinely
///         unlock the same live proof locally, per this project's own established smoke-test
///         discipline (passwordless <c>sudo</c> is available in this project's own development
///         environment for exactly this).
///     </para>
///     <para>
///         <b>This class is a genuinely restartable <see cref="IDhcpProtocolService" /></b>, not a
///         <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> -- <see cref="StartAsync" />/
///         <see cref="StopAsync" /> can be called repeatedly, in either order, for the rest of this
///         process's life, each genuinely binding/releasing a real raw socket (and joining/leaving
///         its multicast group membership on every usable interface) rather than running once for
///         the whole host lifetime. It is registered as an ordinary dependency-injection singleton
///         (never as an <see cref="Microsoft.Extensions.Hosting.IHostedService" /> itself) --
///         <c>Icmpv6RaListenerHostedService</c> is the thin wrapper that starts this instance once,
///         automatically, at host boot, only when <see cref="RaProtocolOptions.Enable" /> is
///         <see langword="true" />; see that class's own doc comment, and
///         <see cref="IDhcpProtocolService" />'s, for why configured <c>Enable</c> and this
///         instance's own live running state are two distinct concepts from here on. A concurrent
///         <see cref="StartAsync" />/<see cref="StopAsync" /> pair is serialized through
///         <see cref="_lifecycleLock" />, and each call is a safe, harmless no-op when this service
///         is already in the state being requested. Both the receive loop and the periodic
///         unsolicited-send loop are torn down together on <see cref="StopAsync" /> and rebuilt
///         together on the next <see cref="StartAsync" /> -- there is exactly one execution task
///         covering both, so a Stop-then-Start cycle can never leave a stale loop of either kind
///         still running alongside a fresh one.
///     </para>
///     <para>
///         <b>Hop Limit 255 is mandatory</b>: RFC 4861 Sections 6.1.1/6.1.2 require every Neighbor
///         Discovery message to carry IPv6 Hop Limit 255, and require a receiver to silently
///         discard one that does not -- a security check ensuring the message could not have been
///         forwarded by an intermediate router (i.e., it genuinely originated on this exact link).
///         This listener sets both the unicast and multicast outbound hop limit to 255 once, at
///         socket-creation time, rather than per datagram.
///     </para>
///     <para>
///         <b>A solicited reply never learns which interface the solicitation actually arrived
///         on</b>: recovering that would need <c>IPV6_RECVPKTINFO</c>/<c>ReceiveMessageFromAsync</c>,
///         which this sub-phase's own live proof (a throwaway, root-run scratchpad console app,
///         per this class's own doc comment above) found genuinely unsupported for a raw
///         <c>IPPROTO_ICMPV6</c> socket on this project's own real target platform (Linux),
///         failing with <c>ENOPROTOOPT</c> ("Protocol not available") -- a real platform
///         limitation, not a bug to work around with more code. This listener therefore never asks
///         for packet information at all: a solicitation carrying a real (non-unspecified) source
///         address is always answered by a plain unicast reply, needing no interface selection at
///         all since the address's own embedded IPv6 zone/scope ID already routes it correctly;
///         only the genuinely rare case of a solicitation sent from the unspecified address
///         (<c>::</c>, before its own sender has configured any address of its own yet) -- which
///         RFC 4861 Section 6.2.6 requires answering by multicast -- falls back to sending that
///         multicast reply on every usable interface at once, rather than only the one true
///         arrival interface, trading a handful of harmless duplicate replies on this project's
///         own primary deployment target (a single relevant LAN interface) for not needing an
///         unsupported socket option at all.
///     </para>
///     <para>
///         <b>Each usable interface can genuinely advertise its own distinct content</b>: every
///         call site that already knows which interface a send is for (the periodic unsolicited
///         loop, and the unspecified-source-address multicast-reply fallback above) passes that
///         interface's own OS-reported name into <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />,
///         which resolves that one interface's own <c>Ninlil.Dhcp.Server.Ra.RaInterfaceOverrideOptions</c>
///         entry, if any is configured, and answers with that interface's own effective content
///         (or, for an explicitly excluded interface, <see langword="null" />, sending nothing at
///         all). The one exception is the plain-unicast-reply path immediately above, which -- for
///         exactly the platform-limitation reason just documented -- can never attribute a real
///         solicitation to a specific arrival interface, and therefore always answers with the
///         global default content, unable to honor either a per-interface override or an
///         exclusion for that one reply.
///     </para>
/// </summary>
/// <param name="advertisementProvider">
///     The provider asked for a Router Advertisement's own current content, every time one is
///     about to be sent.
/// </param>
/// <param name="options">This transport's own timing configuration.</param>
/// <param name="logger">The logger this listener records unexpected socket failures to.</param>
public sealed class Icmpv6RaListener(
    IRaAdvertisementProvider advertisementProvider,
    IOptions<RaProtocolOptions> options,
    ILogger<Icmpv6RaListener> logger
) : IDhcpProtocolService
{
    /// <summary>
    ///     RFC 4861 Section 10's own "MAX_RA_DELAY_TIME" -- the longest a router may wait before
    ///     sending a solicited Router Advertisement, so real replies from every router on a busy
    ///     link do not all land at exactly the same instant.
    /// </summary>
    private static readonly TimeSpan MaximumSolicitedResponseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     RFC 4861 Section 10's own "MIN_DELAY_BETWEEN_RAS" -- the shortest gap this listener
    ///     ever leaves between two Router Advertisements it sends, whether unsolicited or
    ///     solicited, so a burst of solicitations never turns into a flood of replies. Applied
    ///     globally across this whole listener, not per interface -- see this class's own doc
    ///     comment on why a solicited reply cannot learn which interface it is answering on
    ///     anyway, the same "single relevant LAN interface" simplification this reasoning already
    ///     extends to.
    ///     <para>
    ///         RFC 4861 Section 6.2.6 itself calls for scheduling a deferred reply for the end of
    ///         this window rather than dropping it outright; this listener instead simply skips a
    ///         solicited reply that would land inside the window (see
    ///         <see cref="SendAdvertisementAsync" />), a deliberate simplification -- the client's
    ///         own retransmitted solicitation, or this listener's own next periodic unsolicited
    ///         advertisement (already at most <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" />
    ///         away), reaches it soon enough regardless, so the real cost of not deferring is
    ///         negligible against the added bookkeeping a genuine deferred-send queue would need.
    ///     </para>
    /// </summary>
    private static readonly TimeSpan MinimumDelayBetweenAdvertisements = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     The Neighbor Discovery-mandated outbound hop limit every message this listener sends
    ///     must carry -- see this class's own doc comment.
    /// </summary>
    private const int RequiredHopLimit = 255;

    /// <summary>
    ///     This constant contains the largest ICMPv6 datagram this listener will ever read.
    /// </summary>
    private const int MaximumDatagramSize = 1500;

    private static readonly Random Jitter = Random.Shared;

    /// <summary>
    ///     This lock serializes every <see cref="StartAsync" />/<see cref="StopAsync" /> call
    ///     against each other -- the same reasoning
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener._lifecycleLock</c>'s own doc comment
    ///     gives.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains the listening/sending socket, once <see cref="StartAsync" /> has
    ///     bound one, cleared again by <see cref="StopAsync" /> once it has been released.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source that owns the currently running receive and
    ///     unsolicited-send loops' own shared lifetime, cancelled by <see cref="StopAsync" /> to
    ///     unblock their pending operations and signal both to exit together.
    /// </summary>
    private CancellationTokenSource _executionCts;

    /// <summary>
    ///     This field contains the currently running execution task -- both the receive loop and
    ///     the periodic unsolicited-send loop, run together via <see cref="Task.WhenAll(Task[])" />
    ///     -- awaited by <see cref="StopAsync" /> to confirm both have genuinely exited (and, with
    ///     them, that no further use of <see cref="_socket" /> is still in flight) before that
    ///     socket is disposed.
    /// </summary>
    private Task _executionTask;

    /// <summary>
    ///     This property tracks the last time this listener sent any Router Advertisement at all
    ///     -- see <see cref="MinimumDelayBetweenAdvertisements" />'s own doc comment for why this
    ///     is one shared timestamp rather than one per interface/destination. Reset to
    ///     <see cref="DateTime.MinValue" /> on every <see cref="StartAsync" /> call, so a restart
    ///     never inherits a stale rate-limit window from a previous run.
    /// </summary>
    private DateTime _lastSentAtUtc = DateTime.MinValue;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting datagrams.
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
            // Calling Start while already running is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
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

                // Every Neighbor Discovery message this listener sends -- unicast or multicast --
                // must carry Hop Limit 255. .NET's own Ttl property maps to IPv6's unicast hop
                // limit; MulticastTimeToLive is the separate outbound value used for a multicast
                // send.
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
                // A genuine failure to create/configure/bind this raw socket (most commonly, on a
                // typical unprivileged process, a permission failure creating a SOCK_RAW socket
                // at all) must leave this service reporting IsListening = false, not
                // half-started -- propagate the exception to the caller rather than swallowing
                // it, exactly the same contract IDhcpProtocolService.StartAsync documents for a
                // genuine bind failure.
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
            // Calling Stop while already stopped is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
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

        // Cancel both loops' own shared token and close the socket -- closing is what actually
        // unblocks a pending ReceiveFromAsync call, and also drops this socket's own
        // multicast-group membership on every interface it joined, since that membership is
        // scoped to the socket's own lifetime.
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
                // Either this call's own cancellationToken fired while waiting, or the loop task
                // itself completed via cancellation -- either way, both loops have stopped
                // touching the socket by the time this method returns, which is this method's
                // own real contract.
            }
        }

        executionCts.Dispose();
        socket.Dispose();

        logger.LogInformation(
            "The Router Advertisement listener stopped and released its bound socket."
        );
    }

    /// <summary>
    ///     This method runs one start/stop cycle's own receive loop and periodic unsolicited-send
    ///     loop together, exiting only once both have genuinely stopped -- mirroring
    ///     <c>Dhcpv6UdpListener.RunReceiveLoopAsync</c>'s own "pass every socket/cancellation value
    ///     explicitly rather than read it back off this instance's own fields" discipline, so a
    ///     stale field from a previous cycle can never leak into a currently running loop.
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
            // Expected during a graceful shutdown/restart.
        }
    }

    /// <summary>
    ///     This method joins <see cref="RaMulticastGroups.AllRouters" /> on every network
    ///     interface that is currently up and IPv6-capable -- the same "join everywhere, log and
    ///     skip a single interface that rejects membership" shape
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener.JoinMulticastGroupOnEveryUsableInterface</c>
    ///     already establishes, adapted to this listener's own multicast group.
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
    ///     This method enumerates the index and OS-reported name of every network interface
    ///     currently up, IPv6-capable, and not the loopback interface -- this listener's own
    ///     definition of "usable," shared by the multicast-join step, the periodic unsolicited
    ///     send loop, and the unspecified-source-address solicited-reply fallback. The name (see
    ///     <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />'s own doc comment) is
    ///     what resolves that one interface's own <c>RaInterfaceOverrideOptions</c> entry, if any
    ///     is configured, letting each interface genuinely advertise its own distinct content
    ///     rather than one shared advertisement for every interface alike.
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
    ///     This method runs this listener's own periodic unsolicited Router Advertisement loop,
    ///     per RFC 4861 Section 6.2.4: on every usable interface, independently, send a fresh
    ///     advertisement to <see cref="RaMulticastGroups.AllNodes" />, then wait a duration chosen
    ///     uniformly at random between <see cref="RaProtocolOptions.MinRouterAdvertisementInterval" />
    ///     and <see cref="RaProtocolOptions.MaxRouterAdvertisementInterval" /> before the next one
    ///     -- the randomization is what keeps multiple routers on the same link from ever settling
    ///     into a synchronized advertisement rhythm.
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
    ///     This method chooses this listener's own next unsolicited-send interval uniformly at
    ///     random between the configured minimum and maximum -- RFC 4861 Section 6.2.4's own
    ///     randomization rule.
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
    ///     This method runs this listener's own receive loop: reading every inbound ICMPv6
    ///     datagram, silently ignoring anything that is not a Router Solicitation (a raw ICMPv6
    ///     socket receives every ICMPv6 message type on the host, not merely the ones this
    ///     listener cares about -- logging each one would be pure noise), and replying to a
    ///     genuine solicitation per RFC 4861 Section 6.2.6.
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

            // A raw ICMPv6 socket delivers every ICMPv6 message type -- silently skip anything
            // that is not a Router Solicitation rather than attempting (and failing, loudly) to
            // decode it as one.
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
    ///     This method handles one genuine Router Solicitation: waits RFC 4861 Section 6.2.6's own
    ///     randomized delay (0 to <see cref="MaximumSolicitedResponseDelay" />), then sends a fresh
    ///     Router Advertisement back -- unicast directly to the soliciting host when it presented
    ///     a real source address (needing no interface selection at all, since that address's own
    ///     embedded zone/scope ID already routes the reply correctly), or multicast to
    ///     <see cref="RaMulticastGroups.AllNodes" /> on every usable interface when it was sent
    ///     from the unspecified address (<c>::</c>) -- see this class's own doc comment for why
    ///     that case cannot instead target only the one true arrival interface. Dispatched as a
    ///     detached task from <see cref="ReceiveLoopAsync" /> so waiting out the response delay for
    ///     one solicitation never blocks receiving the next one.
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
                // This unicast reply cannot be attributed to a specific arrival interface at all
                // (see this class's own doc comment) -- it is therefore always sent with
                // interfaceName: null, meaning it always uses the global default content, and can
                // never honor a would-be per-interface override or exclusion. This is a real,
                // deliberate limitation this reply path shares with -- and extends -- the class's
                // own already-documented inability to select an outbound interface at all for a
                // genuine unicast reply.
                await SendAdvertisementAsync(
                    socket,
                    interfaceIndex: null,
                    interfaceName: null,
                    solicitationSource,
                    stoppingToken
                );
                return;
            }

            // RFC 4861 Section 6.2.6: a solicitation sent from the unspecified address must be
            // answered by multicast -- sent on every usable interface, since this listener cannot
            // recover which one the solicitation actually arrived on (see this class's own doc
            // comment). Each interface's own name is known here, though, so each one still
            // receives its own effective content (or, for an explicitly excluded interface, no
            // reply at all -- see IRaAdvertisementProvider.BuildCurrentAdvertisement).
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
            // Expected during a graceful shutdown.
        }
    }

    /// <summary>
    ///     This method asks <see cref="IRaAdvertisementProvider" /> for the current advertisement
    ///     and sends it to a destination, enforcing <see cref="MinimumDelayBetweenAdvertisements" />
    ///     regardless of whether this particular send was triggered by the periodic loop or a real
    ///     solicitation.
    /// </summary>
    /// <param name="socket">The already-bound socket to send this advertisement over.</param>
    /// <param name="interfaceIndex">
    ///     The interface to select as this send's own outbound multicast interface, or
    ///     <see langword="null" /> for a plain unicast send, which needs no explicit interface
    ///     selection at all since <paramref name="destination" />'s own embedded zone/scope ID
    ///     already determines it.
    /// </param>
    /// <param name="interfaceName">
    ///     The OS-reported name of <paramref name="interfaceIndex" />'s own interface, used to
    ///     resolve that interface's own content override, or <see langword="null" /> when the
    ///     caller does not know (or, for a plain unicast reply, cannot know) which interface this
    ///     send is really for -- see <see cref="IRaAdvertisementProvider.BuildCurrentAdvertisement" />'s
    ///     own doc comment for exactly what <see langword="null" /> means there.
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
