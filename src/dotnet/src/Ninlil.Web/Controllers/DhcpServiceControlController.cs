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
///     This controller administers a real, in-process hot start/stop lifecycle for the four
///     DHCP-family transport listeners -- DHCPv4, DHCPv6, Router Advertisement, and the TFTP
///     server -- through the shared <see cref="IDhcpProtocolService" /> contract each one
///     implements (see that interface's own doc comment). Stopping one genuinely unbinds its
///     socket right now, in this running process, and starting it again genuinely rebinds it, with
///     no process restart involved.
///     <para>
///         <b>This is deliberately a separate REST surface from <see cref="ServicesController" />'s
///         own general <c>/api/services</c> Service Lifecycle Management endpoint</b>, not an
///         oversight -- <c>IDhcpProtocolService</c> (Phase 17a/17b) and
///         <c>Ninlil.Dns.Server.Core.Services.IManagedServiceRegistration</c> (Phase 5l) are two
///         independently evolved abstractions with a real, structural difference this controller
///         depends on directly: a DHCP-family service's own configured <c>Enable</c> flag and its
///         live <see cref="DhcpServiceStatus.IsRunning" /> signal are always reported
///         <em>together</em>, as two genuinely distinct fields, in every response this controller
///         returns -- exactly what an operator managing a family of listeners each with its own
///         restart-required "enable at boot" setting needs to see at a glance --
///         whereas <c>ManagedServiceSummary</c> (the general registry's own response shape)
///         collapses that same distinction down to <c>IsRunning</c> alone. Folding these four
///         services into the general registry instead would have meant either losing that
///         side-by-side <c>Enabled</c>/<c>IsRunning</c> reporting, or extending
///         <c>ManagedServiceSummary</c> itself for every one of that registry's other ~26 members
///         that have no comparable "configured but not auto-started" concept at all. A dedicated
///         controller keeps both shapes honest.
///     </para>
///     <para>
///         <b>Route shape</b>: <see cref="Start" />/<see cref="Stop" /> address a service by
///         <see cref="DhcpServiceKindEnum" /> as a typed route segment (bound case-insensitively by
///         ASP.NET Core's own default enum model binding), rather than <see cref="ServicesController" />'s
///         own free-text service-name lookup -- there are exactly four, fixed, well-known DHCP-family
///         services, never an open-ended registry an operator could name incorrectly, so an
///         unrecognized <c>{kind}</c> segment is rejected by model binding itself, as an ordinary
///         <c>400</c>, before this controller's own action code ever runs.
///     </para>
///     <para>
///         <b>Permissions</b>: <see cref="List" /> reveals only which of the four services exist,
///         each one's configured <c>Enable</c> flag, and whether it is genuinely running -- no
///         secret or configuration data -- so it is gated by the lighter <c>dhcpservices:read</c>.
///         <see cref="Start" />/<see cref="Stop" /> genuinely change what this server is doing on
///         the network right now (stopping DHCPv4/DHCPv6 live cuts off real client lease traffic
///         immediately), so both are gated by the separate, narrower <c>dhcpservices:manage</c> --
///         the same "a real start/stop action needs its own dedicated permission, apart from
///         ordinary <c>&lt;resource&gt;:write</c>" precedent <see cref="ServicesController" />'s
///         own <c>services:manage</c>/<see cref="StorageController" />'s own <c>storage:manage</c>
///         already establish.
///     </para>
/// </summary>
/// <param name="services">
///     Every registered <see cref="IDhcpProtocolService" /> -- resolved once per request and
///     matched by concrete type, the same "resolve by type, not by a separate registry" shape
///     <c>TransportListenersHealthCheck</c> already establishes for the DNS side's own transports.
/// </param>
/// <param name="dhcpv4Options">DHCPv4's own configured <c>Enable</c> flag.</param>
/// <param name="dhcpv6Options">DHCPv6's own configured <c>Enable</c> flag.</param>
/// <param name="raOptions">Router Advertisement's own configured <c>Enable</c> flag.</param>
/// <param name="tftpOptions">The TFTP server's own configured <c>Enable</c> flag.</param>
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
    ///     This action reports every DHCP-family service's own configured <c>Enable</c> flag and
    ///     its actual, live running state -- see this class's own doc comment for why both are
    ///     always reported side by side rather than collapsed into one signal.
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
    ///     This action starts a single DHCP-family service -- binding its socket -- right now, in
    ///     this running process, regardless of its own configured <c>Enable</c> flag. Starting a
    ///     service that is already running is a safe, harmless no-op (see
    ///     <see cref="IDhcpProtocolService.StartAsync" />'s own doc comment).
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
    ///     This action stops a single DHCP-family service -- releasing its socket -- right now, in
    ///     this running process, leaving every other DHCP-family (and every other) service this
    ///     server runs untouched. Stopping a service that is already stopped is a safe, harmless
    ///     no-op (see <see cref="IDhcpProtocolService.StopAsync" />'s own doc comment).
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
    ///     This method resolves <paramref name="kind" /> to its one real registered
    ///     <see cref="IDhcpProtocolService" /> instance, throwing <see cref="NotFoundException" />
    ///     in the (should-be-unreachable, given every one of the four is registered
    ///     unconditionally by this project's own composition root) case that none is found for a
    ///     structurally valid <see cref="DhcpServiceKindEnum" /> value.
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
    ///     This method reports <paramref name="kind" />'s own configured <c>Enable</c> flag.
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
    ///     This method converts one resolved service (or, defensively, its absence) into its REST
    ///     representation.
    /// </summary>
    private static DhcpServiceStatus ToStatus(
        DhcpServiceKindEnum kind,
        IDhcpProtocolService service,
        bool enabled
    ) => new(kind, enabled, service?.IsRunning ?? false);
}
