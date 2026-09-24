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
///     This class implements the "each enabled DHCP-family service is actually running" health
///     check, a tenth addition to <c>GET /status</c>'s own checklist -- the DHCP analogue of
///     <see cref="TransportListenersHealthCheck" />, adapted from that check's own DNS transports
///     to this project's four DHCP-family <see cref="IDhcpProtocolService" /> implementations
///     (DHCPv4, DHCPv6, Router Advertisement, TFTP). A service that is disabled entirely (its own
///     configured <c>Enable</c> flag is <see langword="false" />, and it was never started live
///     through <see cref="Controllers.DhcpServiceControlController" /> either) is simply omitted
///     from this check's own description, not reported as unhealthy -- an operator who has
///     deliberately left DHCP off entirely must never see a red health check for it.
///     <para>
///         <b>Unhealthy, mirroring <see cref="TransportListenersHealthCheck" />'s own precedent for
///         the identical shape of problem, never Degraded</b>: a service whose own configured
///         <c>Enable</c> flag is <see langword="true" /> but whose live
///         <see cref="IDhcpProtocolService.IsRunning" /> signal reports
///         <see langword="false" /> is a genuine, actionable operational fault this server's own
///         operator needs to notice -- a socket that failed to bind at boot (a port conflict, most
///         plausibly, or -- Router Advertisement only -- a missing <c>CAP_NET_RAW</c> grant), or a
///         service an operator (or an automated caller) stopped live through
///         <see cref="Controllers.DhcpServiceControlController" /> and then forgot was still
///         configured <c>Enable: true</c>. This check cannot distinguish a deliberate live stop
///         from a genuine crash -- neither can <see cref="TransportListenersHealthCheck" /> for the
///         DNS side's own transports -- but "configured on, not actually serving" is unhealthy
///         either way: real clients on the network are not getting real DHCP/Router
///         Advertisement/PXE-boot service right now, exactly as configured to expect it.
///     </para>
/// </summary>
/// <param name="services">Every registered <see cref="IDhcpProtocolService" />, matched by concrete type.</param>
/// <param name="dhcpv4Options">DHCPv4's own configured <c>Enable</c> flag.</param>
/// <param name="dhcpv6Options">DHCPv6's own configured <c>Enable</c> flag.</param>
/// <param name="raOptions">Router Advertisement's own configured <c>Enable</c> flag.</param>
/// <param name="tftpOptions">The TFTP server's own configured <c>Enable</c> flag.</param>
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

        void Evaluate(string name, bool enabled, bool isRunning)
        {
            if (!enabled)
                return;

            if (isRunning)
                running.Add(name);
            else
                notRunning.Add(name);
        }

        Evaluate(
            "DHCPv4",
            dhcpv4Options.Value.Enable,
            resolved.OfType<Dhcpv4UdpListener>().FirstOrDefault()?.IsListening ?? false
        );
        Evaluate(
            "DHCPv6",
            dhcpv6Options.Value.Enable,
            resolved.OfType<Dhcpv6UdpListener>().FirstOrDefault()?.IsListening ?? false
        );
        Evaluate(
            "Router Advertisement",
            raOptions.Value.Enable,
            resolved.OfType<Icmpv6RaListener>().FirstOrDefault()?.IsListening ?? false
        );
        Evaluate(
            "TFTP",
            tftpOptions.Value.Enable,
            resolved.OfType<TftpServer>().FirstOrDefault()?.IsListening ?? false
        );

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
