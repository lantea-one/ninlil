using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Examples.Services;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller serves <c>/api/services</c>: starting, stopping and restarting the DNS
///     transport listeners and background maintenance loops that
///     <see cref="IManagedServiceRegistry" /> tracks, inside the running process. Stopping a
///     transport unbinds its socket at once and starting it rebinds it, with no process restart.
///     The calls reach the same instances the ASP.NET Core host runs (see
///     <see cref="ManagedServiceRegistry" />).
///     <para>
///         <b>This is one of the most dangerous endpoints in the API.</b> Stopping the wrong
///         transport cuts off client DNS resolution immediately, and stopping the wrong
///         background loop can let a zone's DNSSEC signatures expire, a secondary zone go stale or
///         a filter list stop refreshing, with no other safeguard. So <see cref="Start" />,
///         <see cref="Stop" /> and <see cref="Restart" /> need a separate
///         <c>services:manage</c> permission rather than the usual <c>resource:write</c>, and a
///         role can be given broad configuration write access without the power to take DNS
///         offline. <see cref="List" /> reveals only which services exist and whether each is
///         running, so it needs only <c>services:read</c>.
///     </para>
/// </summary>
/// <param name="registry">The registry used to list, start and stop services.</param>
[ApiController]
[Route("api/services")]
public class ServicesController(IManagedServiceRegistry registry) : ControllerBase
{
    /// <summary>
    ///     This action lists every managed service and whether it is running, sorted by category
    ///     and then by name (the registry itself guarantees no order). Requires
    ///     <c>services:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("services:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ManagedServiceSummaryListExample))]
    public ActionResult<IReadOnlyList<ManagedServiceSummary>> List() =>
        Ok(
            registry
                .Services.OrderBy(service => service.Category)
                .ThenBy(service => service.Name, StringComparer.Ordinal)
                .Select(ToSummary)
                .ToList()
        );

    /// <summary>
    ///     This action starts one service now: it binds a transport's socket or resumes a
    ///     background loop. Starting a service that is already running returns its state and changes
    ///     nothing. A service that was started but is down, such as a transport that declined to
    ///     bind, is stopped and started again, which retries it. Returns 404 for an unknown name. Requires
    ///     <c>services:manage</c>.
    /// </summary>
    /// <param name="name">The service to start.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("{name}/start")]
    [RequirePermission("services:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ManagedServiceSummaryExample))]
    public async Task<ActionResult<ManagedServiceSummary>> Start(
        string name,
        CancellationToken stoppingToken
    )
    {
        IManagedServiceRegistration service = Require(name);
        await service.StartAsync(stoppingToken);
        return Ok(ToSummary(service));
    }

    /// <summary>
    ///     This action stops one service now, unbinding a transport's socket or halting a
    ///     background loop, and leaves every other service running. Returns 404 for an unknown
    ///     name. Requires <c>services:manage</c>.
    /// </summary>
    /// <param name="name">The service to stop.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("{name}/stop")]
    [RequirePermission("services:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ManagedServiceSummaryStoppedExample))]
    public async Task<ActionResult<ManagedServiceSummary>> Stop(
        string name,
        CancellationToken stoppingToken
    )
    {
        IManagedServiceRegistration service = Require(name);
        await service.StopAsync(stoppingToken);
        return Ok(ToSummary(service));
    }

    /// <summary>
    ///     This action stops one service and starts it again. Services that reload their state on
    ///     every start pick up changes this way: <c>AuthoritativeZoneCache</c> and
    ///     <c>SignedZoneCache</c> reload their whole in-memory snapshot from storage, and their
    ///     stop leaves the loaded snapshot answering, so for them only a restart has a visible
    ///     effect. A transport listener re-reads its port, certificate and tuning settings. Returns
    ///     404 for an unknown name. Requires <c>services:manage</c>.
    /// </summary>
    /// <param name="name">The service to restart.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("{name}/restart")]
    [RequirePermission("services:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ManagedServiceSummaryExample))]
    public async Task<ActionResult<ManagedServiceSummary>> Restart(
        string name,
        CancellationToken stoppingToken
    )
    {
        IManagedServiceRegistration service = Require(name);
        await service.StopAsync(stoppingToken);
        await service.StartAsync(stoppingToken);
        return Ok(ToSummary(service));
    }

    /// <summary>
    ///     This method finds a registered service by name, or throws
    ///     <see cref="NotFoundException" /> (404) when there is none.
    /// </summary>
    private IManagedServiceRegistration Require(string name) =>
        registry.Find(name) ?? throw new NotFoundException("ManagedService", name);

    /// <summary>
    ///     This method converts a registration to its API summary.
    /// </summary>
    private static ManagedServiceSummary ToSummary(IManagedServiceRegistration service) =>
        new(service.Name, service.Category, service.IsRunning);
}
