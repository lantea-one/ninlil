using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Tftp;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>dhcpServices</c> health check of <c>GET /status</c>: whether
///     each DHCP-family service that is enabled in the configuration is running. It is the DHCP
///     counterpart of <see cref="TransportListenersHealthCheck" />, covering the four
///     <see cref="IDhcpProtocolService" /> implementations (DHCPv4, DHCPv6, Router Advertisement,
///     TFTP). A service whose <c>Enable</c> flag is off is left out, even if it was started
///     through <see cref="Controllers.DhcpServiceControlController" />, so a server without DHCP
///     never shows a failing check for it.
///     <para>
///         <b>Unhealthy, not Degraded</b>, as in <see cref="TransportListenersHealthCheck" />: a
///         service enabled in the configuration but not running means clients are not getting
///         DHCP, Router Advertisement or PXE boot service. The cause may be a socket that failed
///         to bind (usually a port conflict, or for Router Advertisement a missing
///         <c>CAP_NET_RAW</c>), a TFTP server with no usable root directory, or a service stopped
///         through <see cref="Controllers.DhcpServiceControlController" />. A bind failure is
///         named after the service in parentheses (for example "DHCPv4 (Address already in
///         use)"), taken from the service's <c>StartFailure</c>; the other causes show the name
///         alone. A bind failure at boot does not stop Ninlil: the hosted service logs one Error
///         and the check reports it. The <c>Enable</c> flags come from the settings loaded at
///         startup.
///     </para>
/// </summary>
/// <param name="services">Every registered <see cref="IDhcpProtocolService" />, matched by concrete type.</param>
/// <param name="dhcpv4Options">The DHCPv4 settings, for the <c>Enable</c> flag.</param>
/// <param name="dhcpv6Options">The DHCPv6 settings, for the <c>Enable</c> flag.</param>
/// <param name="raOptions">The Router Advertisement settings, for the <c>Enable</c> flag.</param>
/// <param name="tftpOptions">The TFTP settings, for the <c>Enable</c> flag.</param>
public sealed class DhcpServiceHealthCheck(
    IEnumerable<IDhcpProtocolService> services,
    IOptions<Dhcpv4ProtocolOptions> dhcpv4Options,
    IOptions<Dhcpv6ProtocolOptions> dhcpv6Options,
    IOptions<RaProtocolOptions> raOptions,
    IOptions<TftpServerOptions> tftpOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<IDhcpProtocolService> resolved = [.. services];
        List<string> notRunning = [];
        List<string> running = [];

        void Evaluate(string name, bool enabled, bool isRunning, string startFailure)
        {
            if (!enabled)
                return;

            if (isRunning)
                running.Add(name);
            else
                notRunning.Add(
                    string.IsNullOrWhiteSpace(startFailure) ? name : $"{name} ({startFailure})"
                );
        }

        Dhcpv4UdpListener dhcpv4 = resolved.OfType<Dhcpv4UdpListener>().FirstOrDefault();
        Dhcpv6UdpListener dhcpv6 = resolved.OfType<Dhcpv6UdpListener>().FirstOrDefault();
        Icmpv6RaListener ra = resolved.OfType<Icmpv6RaListener>().FirstOrDefault();
        TftpServer tftp = resolved.OfType<TftpServer>().FirstOrDefault();

        Evaluate(
            "DHCPv4",
            dhcpv4Options.Value.Enable,
            dhcpv4?.IsListening ?? false,
            dhcpv4?.StartFailure
        );
        Evaluate(
            "DHCPv6",
            dhcpv6Options.Value.Enable,
            dhcpv6?.IsListening ?? false,
            dhcpv6?.StartFailure
        );
        Evaluate(
            "Router Advertisement",
            raOptions.Value.Enable,
            ra?.IsListening ?? false,
            ra?.StartFailure
        );
        Evaluate("TFTP", tftpOptions.Value.Enable, tftp?.IsListening ?? false, tftp?.StartFailure);

        if (notRunning.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"{notRunning.Count} enabled DHCP-family service(s) are not running: "
                        + string.Join(", ", notRunning)
                        + $". Running: {(running.Count > 0 ? string.Join(", ", running) : "none")}."
                )
            );

        if (running.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No DHCP-family service is enabled."));

        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"Every enabled DHCP-family service is running: {string.Join(", ", running)}."
            )
        );
    }
}
