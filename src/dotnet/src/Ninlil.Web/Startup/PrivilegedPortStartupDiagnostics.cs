using Microsoft.Extensions.Logging;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class solves a real, previously undiagnosed operator confusion: binding a
///     "privileged" port (below 1024 on Linux/most Unix-likes -- 53 for Plain DNS, 80/443 for
///     this host's own Kestrel listener, 853 for DNS-over-TLS/DNS-over-QUIC, or a DNSCrypt port
///     reconfigured below 1024) without the elevation actually needed to bind it does not fail
///     with any obviously actionable message today -- the affected transport's own
///     <c>BackgroundService.ExecuteAsync</c> simply throws a raw <see cref="System.Net.Sockets.SocketException" />
///     moments after this host finishes starting, which (absent this diagnostic) is the very
///     first and only signal an operator ever sees.
///     <para>
///         This class runs once, immediately after the host is built but before any transport's
///         own listener actually starts, and warns -- through the real, already-configured
///         logger, not the throwaway bootstrap logger earlier startup code uses before dependency
///         injection exists -- for every currently-enabled listener whose configured port is
///         privileged and which a live bind probe (<see cref="IPrivilegedPortBindProbe" />)
///         reports this process cannot actually bind. <b>This is diagnostic only</b>: it never
///         prevents, delays, or otherwise changes what happens next -- every listener still
///         attempts its own real bind exactly as it always has, succeeding or failing entirely on
///         its own, whether or not this class warned about it first.
///     </para>
///     <para>
///         A live probe, not a bare "is the port number below 1024" rule, is deliberate: a
///         container or a systemd unit can grant a non-root process Linux's own
///         <c>CAP_NET_BIND_SERVICE</c> capability, in which case a configured privileged port is
///         entirely fine and this class must stay silent about it -- see
///         <see cref="SocketPrivilegedPortBindProbe" />'s own doc comment for exactly how that
///         live check works.
///     </para>
/// </summary>
public static class PrivilegedPortStartupDiagnostics
{
    /// <summary>
    ///     This constant contains the highest port number POSIX/Linux still treats as
    ///     "privileged" -- 1023 -- so a port at or below it may need elevation to bind, and a
    ///     port above it never does.
    /// </summary>
    private const int HighestPrivilegedPort = 1023;

    /// <summary>
    ///     This method builds the real, current list of every listener this host is about to
    ///     start -- honoring each transport's own <c>Enable</c> flag exactly the way
    ///     <c>Program.cs</c>'s own composition root already does, so this diagnostic never reports
    ///     against a transport an operator has not actually turned on. Kept as a small, pure
    ///     function (no socket, no logger, no DI container) specifically so it can be exercised
    ///     directly by a unit test.
    /// </summary>
    /// <param name="webListenOptions">
    ///     This host's own shared Kestrel HTTP/HTTPS listener configuration -- always included,
    ///     since neither the HTTP nor the HTTPS port has an <c>Enable</c> flag of its own; both
    ///     always bind.
    /// </param>
    /// <param name="plainProtocolOptions">
    ///     Plain DNS's own UDP/TCP listener configuration -- always included, the one DNS
    ///     transport with no <c>Enable</c> flag of its own (see <c>PlainProtocolOptions</c>'s own
    ///     doc comment).
    /// </param>
    /// <param name="dnsOverTlsOptions">
    ///     DNS-over-TLS's own listener configuration -- included only when
    ///     <see cref="DnsOverTlsOptions.Enable" /> is <see langword="true" />.
    /// </param>
    /// <param name="dnsOverQuicOptions">
    ///     DNS-over-QUIC's own listener configuration -- included only when
    ///     <see cref="DnsOverQuicOptions.Enable" /> is <see langword="true" />.
    /// </param>
    /// <param name="dnsCryptOptions">
    ///     DNSCrypt's own listener configuration -- included only when
    ///     <see cref="DnsCryptOptions.Enable" /> is <see langword="true" />. DNS-over-HTTPS is
    ///     deliberately never included: in this host's own composition, it rides this same
    ///     <paramref name="webListenOptions" /> listener rather than binding a port of its own
    ///     (see <c>EndpointRouteBuilderDnsOverHttpsExtension</c>'s own doc comment), so
    ///     <c>DnsOverHttpsOptions.Port</c> is not a real, separately-bound port here at all.
    /// </param>
    /// <returns>Every listener this host is about to actually start, by name and port.</returns>
    public static IReadOnlyList<PrivilegedPortListenerDescriptor> BuildConfiguredListeners(
        WebListenOptions webListenOptions,
        PlainProtocolOptions plainProtocolOptions,
        DnsOverTlsOptions dnsOverTlsOptions,
        DnsOverQuicOptions dnsOverQuicOptions,
        DnsCryptOptions dnsCryptOptions
    )
    {
        List<PrivilegedPortListenerDescriptor> listeners =
        [
            new("HTTP", webListenOptions.HttpPort),
            new("HTTPS", webListenOptions.HttpsPort),
            new("Plain DNS (UDP/TCP)", plainProtocolOptions.Port),
        ];

        if (dnsOverTlsOptions.Enable)
            listeners.Add(
                new PrivilegedPortListenerDescriptor("DNS-over-TLS", dnsOverTlsOptions.Port)
            );

        if (dnsOverQuicOptions.Enable)
            listeners.Add(
                new PrivilegedPortListenerDescriptor("DNS-over-QUIC", dnsOverQuicOptions.Port)
            );

        if (dnsCryptOptions.Enable)
            listeners.Add(new PrivilegedPortListenerDescriptor("DNSCrypt", dnsCryptOptions.Port));

        // We're done, return every listener this host is about to actually start.
        return listeners;
    }

    /// <summary>
    ///     This method logs one clear, actionable startup warning for every listener in
    ///     <paramref name="listeners" /> whose own configured port is privileged (1023 or below)
    ///     and which <paramref name="probe" /> reports this process cannot actually bind. Kept
    ///     separate from <see cref="BuildConfiguredListeners" /> so a unit test can supply a fake
    ///     <see cref="IPrivilegedPortBindProbe" /> with a deterministic answer, rather than
    ///     depending on the real OS-level privilege state of whatever account happens to run the
    ///     test.
    /// </summary>
    /// <param name="listeners">Every listener this host is about to actually start.</param>
    /// <param name="probe">The live probe used to test whether each privileged port can actually be bound.</param>
    /// <param name="logger">The logger this method writes an actionable warning to.</param>
    public static void WarnAboutUnbindablePrivilegedPorts(
        IReadOnlyList<PrivilegedPortListenerDescriptor> listeners,
        IPrivilegedPortBindProbe probe,
        ILogger logger
    )
    {
        foreach (PrivilegedPortListenerDescriptor listener in listeners)
        {
            if (listener.Port > HighestPrivilegedPort)
                continue;

            if (probe.CanBind(listener.Port))
                continue;

            logger.LogWarning(
                "The {TransportName} listener is configured to bind port {Port}, which is a "
                    + "privileged port on Linux/most Unix-likes, but this process does not appear "
                    + "to have the elevation needed to bind it. This transport will likely fail to "
                    + "start. To fix this, either run this process as root, grant it the "
                    + "CAP_NET_BIND_SERVICE capability (for example: 'sudo setcap "
                    + "cap_net_bind_service=+ep <path-to-binary>', or the equivalent container/"
                    + "systemd unit setting), or reconfigure this transport to use a port above "
                    + "1024 and forward the original port to it externally.",
                listener.TransportName,
                listener.Port
            );
        }
    }
}
