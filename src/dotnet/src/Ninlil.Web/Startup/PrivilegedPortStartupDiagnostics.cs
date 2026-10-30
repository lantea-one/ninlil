using Microsoft.Extensions.Logging;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class warns at startup when a listener is configured on a privileged port (below 1024
///     on Linux and most Unix-likes: 53 for plain DNS, 80 and 443 for Kestrel, 853 for DNS-over-TLS
///     and DNS-over-QUIC, or a DNSCrypt port moved below 1024) that the process cannot bind.
///     Without it, the only sign is a raw <see cref="System.Net.Sockets.SocketException" /> from
///     the transport's <c>BackgroundService.ExecuteAsync</c> shortly after startup.
///     <para>
///         <c>Program.cs</c> runs it once, after the host is built and before the listeners
///         start, and logs through the configured logger (not the bootstrap logger used before
///         dependency injection exists). It warns for each enabled listener whose port is
///         privileged and which a live bind probe (<see cref="IPrivilegedPortBindProbe" />) says
///         cannot be bound. <b>It only diagnoses:</b> every listener still tries its own bind
///         and succeeds or fails on its own.
///     </para>
///     <para>
///         A live probe is used rather than a "below 1024" rule because a container or systemd
///         unit can give a non-root process Linux's <c>CAP_NET_BIND_SERVICE</c>, in which case a
///         privileged port is fine and no warning should appear (see
///         <see cref="SocketPrivilegedPortBindProbe" />).
///     </para>
/// </summary>
public static class PrivilegedPortStartupDiagnostics
{
    /// <summary>
    ///     This constant contains the highest port Linux treats as privileged, 1023 (ip(7): "port
    ///     numbers below 1024 are called privileged ports"); a port at or below it may need
    ///     elevation to bind.
    /// </summary>
    private const int HighestPrivilegedPort = 1023;

    /// <summary>
    ///     This method lists the listeners the host is about to start, honoring each transport's
    ///     <c>Enable</c> flag as <c>Program.cs</c> does, so no warning concerns a transport that is
    ///     switched off. It is a pure function (no socket, logger or container) so a unit test can
    ///     call it directly.
    /// </summary>
    /// <param name="webListenOptions">
    ///     The Kestrel HTTP and HTTPS listener settings. Both ports are always included, since
    ///     neither has an <c>Enable</c> flag.
    /// </param>
    /// <param name="plainProtocolOptions">
    ///     The plain DNS UDP/TCP listener settings, always included: it is the one DNS transport
    ///     with no <c>Enable</c> flag (see <c>PlainProtocolOptions</c>).
    /// </param>
    /// <param name="dnsOverTlsOptions">
    ///     The DNS-over-TLS settings, included only when <see cref="DnsOverTlsOptions.Enable" /> is
    ///     <see langword="true" />.
    /// </param>
    /// <param name="dnsOverQuicOptions">
    ///     The DNS-over-QUIC settings, included only when <see cref="DnsOverQuicOptions.Enable" />
    ///     is <see langword="true" />.
    /// </param>
    /// <param name="dnsCryptOptions">
    ///     The DNSCrypt settings, included only when <see cref="DnsCryptOptions.Enable" /> is
    ///     <see langword="true" />. DNS-over-HTTPS is never included: it is served by the
    ///     <paramref name="webListenOptions" /> listener rather than its own port (see
    ///     <c>EndpointRouteBuilderDnsOverHttpsExtension</c>), so <c>DnsOverHttpsOptions.Port</c> is
    ///     not bound separately.
    /// </param>
    /// <returns>Every listener the host is about to start, by name and port.</returns>
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

        // Every listener the host is about to start.
        return listeners;
    }

    /// <summary>
    ///     This method logs one actionable warning for each listener whose port is privileged (1023
    ///     or below) and which <paramref name="probe" /> says this process cannot bind. It is
    ///     separate from <see cref="BuildConfiguredListeners" /> so a unit test can pass a fake
    ///     <see cref="IPrivilegedPortBindProbe" /> instead of depending on the privileges of the
    ///     account running the test.
    /// </summary>
    /// <param name="listeners">Every listener the host is about to start.</param>
    /// <param name="probe">Tests whether each privileged port can be bound.</param>
    /// <param name="logger">Receives the warnings.</param>
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
