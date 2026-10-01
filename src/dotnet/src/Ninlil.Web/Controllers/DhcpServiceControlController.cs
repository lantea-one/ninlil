using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpServices;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller starts and stops the four DHCP-family listeners (DHCPv4, DHCPv6, Router
///     Advertisement and TFTP) inside the running process, through the
///     <see cref="IDhcpProtocolService" /> each implements. Stopping one unbinds its socket at
///     once and starting it rebinds it, with no process restart.
///     <para>
///         <b>Why it is separate from <see cref="ServicesController" />.</b> Its responses always
///         show a service's configured <c>Enable</c> flag (whether it starts at boot, which needs
///         a restart to change) and its live <see cref="DhcpServiceStatus.IsRunning" /> state side
///         by side. <c>ManagedServiceSummary</c>, the <c>/api/services</c> response, carries only
///         the running state, and the other services there have no "configured but not started"
///         notion, so this pair is kept in its own controller.
///     </para>
///     <para>
///         <see cref="Start" /> and <see cref="Stop" /> take a <see cref="DhcpServiceKindEnum" />
///         route segment, bound case-insensitively, rather than a free-text name: there are only
///         four fixed services, and an unknown <c>{kind}</c> fails model binding with 400.
///     </para>
///     <para>
///         <see cref="List" /> reveals only each service's <c>Enable</c> flag and running state,
///         so it needs <c>dhcpservices:read</c>. <see cref="Start" /> and <see cref="Stop" />
///         change what the server does on the network (stopping DHCPv4 or DHCPv6 cuts off lease
///         traffic at once), so they need the separate <c>dhcpservices:manage</c>, as with
///         <c>services:manage</c> and <c>storage:manage</c>.
///     </para>
/// </summary>
/// <param name="services">
///     Every registered <see cref="IDhcpProtocolService" />, matched by concrete type, as
///     <c>TransportListenersHealthCheck</c> does for the DNS transports.
/// </param>
/// <param name="dhcpv4Options">The DHCPv4 settings loaded at startup, for its <c>Enable</c> flag.</param>
/// <param name="dhcpv6Options">The DHCPv6 settings loaded at startup, for its <c>Enable</c> flag.</param>
/// <param name="raOptions">The Router Advertisement settings loaded at startup, for its <c>Enable</c> flag.</param>
/// <param name="tftpOptions">The TFTP settings loaded at startup, for its <c>Enable</c> flag.</param>
[ApiController]
[Route("api/dhcp-services")]
public class DhcpServiceControlController(
    IEnumerable<IDhcpProtocolService> services,
    IOptions<Dhcpv4ProtocolOptions> dhcpv4Options,
    IOptions<Dhcpv6ProtocolOptions> dhcpv6Options,
    IOptions<RaProtocolOptions> raOptions,
    IOptions<TftpServerOptions> tftpOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reports each DHCP-family service's configured <c>Enable</c> flag and its
    ///     live running state. Requires <c>dhcpservices:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcpservices:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpServiceStatusListExample))]
    public ActionResult<IReadOnlyList<DhcpServiceStatus>> List()
    {
        IReadOnlyList<IDhcpProtocolService> resolved = [.. services];

        return Ok(
            new List<DhcpServiceStatus>
            {
                ToStatus(
                    DhcpServiceKindEnum.Dhcpv4,
                    resolved.OfType<Dhcpv4UdpListener>().FirstOrDefault(),
                    dhcpv4Options.Value.Enable
                ),
                ToStatus(
                    DhcpServiceKindEnum.Dhcpv6,
                    resolved.OfType<Dhcpv6UdpListener>().FirstOrDefault(),
                    dhcpv6Options.Value.Enable
                ),
                ToStatus(
                    DhcpServiceKindEnum.RouterAdvertisement,
                    resolved.OfType<Icmpv6RaListener>().FirstOrDefault(),
                    raOptions.Value.Enable
                ),
                ToStatus(
                    DhcpServiceKindEnum.Tftp,
                    resolved.OfType<TftpServer>().FirstOrDefault(),
                    tftpOptions.Value.Enable
                ),
            }
        );
    }

    /// <summary>
    ///     This action starts one DHCP-family service now, binding its socket, whatever its
    ///     <c>Enable</c> flag says. Starting a running service changes nothing (see
    ///     <see cref="IDhcpProtocolService.StartAsync" />). Returns 400 for an unknown kind.
    ///     Requires <c>dhcpservices:manage</c>.
    /// </summary>
    /// <param name="kind">Which of the four DHCP-family services to start.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("{kind}/start")]
    [RequirePermission("dhcpservices:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpServiceStatusExample))]
    public async Task<ActionResult<DhcpServiceStatus>> Start(
        DhcpServiceKindEnum kind,
        CancellationToken stoppingToken
    )
    {
        IDhcpProtocolService service = Resolve(kind);
        await service.StartAsync(stoppingToken);
        return Ok(ToStatus(kind, service, IsEnabled(kind)));
    }

    /// <summary>
    ///     This action stops one DHCP-family service now, releasing its socket and leaving every
    ///     other service running. Stopping a stopped service changes nothing (see
    ///     <see cref="IDhcpProtocolService.StopAsync" />). Returns 400 for an unknown kind.
    ///     Requires <c>dhcpservices:manage</c>.
    /// </summary>
    /// <param name="kind">Which of the four DHCP-family services to stop.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("{kind}/stop")]
    [RequirePermission("dhcpservices:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpServiceStatusStoppedExample))]
    public async Task<ActionResult<DhcpServiceStatus>> Stop(
        DhcpServiceKindEnum kind,
        CancellationToken stoppingToken
    )
    {
        IDhcpProtocolService service = Resolve(kind);
        await service.StopAsync(stoppingToken);
        return Ok(ToStatus(kind, service, IsEnabled(kind)));
    }

    /// <summary>
    ///     This method finds the registered <see cref="IDhcpProtocolService" /> for
    ///     <paramref name="kind" />, or throws <see cref="NotFoundException" />. All four are
    ///     registered unconditionally in <c>Program.cs</c>, so that should not happen.
    /// </summary>
    private IDhcpProtocolService Resolve(DhcpServiceKindEnum kind)
    {
        IDhcpProtocolService service = kind switch
        {
            DhcpServiceKindEnum.Dhcpv4 => services.OfType<Dhcpv4UdpListener>().FirstOrDefault(),
            DhcpServiceKindEnum.Dhcpv6 => services.OfType<Dhcpv6UdpListener>().FirstOrDefault(),
            DhcpServiceKindEnum.RouterAdvertisement => services
                .OfType<Icmpv6RaListener>()
                .FirstOrDefault(),
            DhcpServiceKindEnum.Tftp => services.OfType<TftpServer>().FirstOrDefault(),
            _ => null,
        };

        return service ?? throw new NotFoundException("DhcpService", kind.ToString());
    }

    /// <summary>
    ///     This method returns the configured <c>Enable</c> flag for <paramref name="kind" />.
    /// </summary>
    private bool IsEnabled(DhcpServiceKindEnum kind) =>
        kind switch
        {
            DhcpServiceKindEnum.Dhcpv4 => dhcpv4Options.Value.Enable,
            DhcpServiceKindEnum.Dhcpv6 => dhcpv6Options.Value.Enable,
            DhcpServiceKindEnum.RouterAdvertisement => raOptions.Value.Enable,
            DhcpServiceKindEnum.Tftp => tftpOptions.Value.Enable,
            _ => false,
        };

    /// <summary>
    ///     This method converts a service, or a missing one, to its API representation.
    /// </summary>
    private static DhcpServiceStatus ToStatus(
        DhcpServiceKindEnum kind,
        IDhcpProtocolService service,
        bool enabled
    ) => new(kind, enabled, service?.IsRunning ?? false);
}
