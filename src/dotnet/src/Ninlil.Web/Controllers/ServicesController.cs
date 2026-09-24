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
///     This controller administers Service Lifecycle Management's own <c>/api/services</c>
///     surface: a real, in-process hot start/stop/restart lifecycle for every DNS transport
///     listener and background maintenance loop <see cref="IManagedServiceRegistry" /> tracks --
///     stopping a transport genuinely unbinds its socket right now, in this running process, and
///     starting it again genuinely rebinds it, with no process restart involved (see
///     <see cref="ManagedServiceRegistry" />'s own doc comment for exactly how this controller's
///     calls reach the identical instances the ASP.NET Core generic host itself manages, not an
///     independent copy).
///     <para>
///         <b>This is one of the most operationally dangerous endpoints in this entire REST API</b>,
///         a deliberate design consideration, not an afterthought: stopping the wrong transport
///         cuts off real client DNS resolution immediately, and stopping the wrong background
///         loop can leave a zone's DNSSEC signatures to silently expire, a secondary zone to drift
///         stale, or a filter list to stop refreshing -- all with no other guardrail in front of
///         it. For that reason, every write action here (<see cref="Start" />/<see cref="Stop" />/
///         <see cref="Restart" />) is gated by a dedicated <c>services:manage</c> permission,
///         deliberately named apart from this project's usual <c>&lt;resource&gt;:write</c>
///         convention (every other controller's own two-permission split, most recently
///         <see cref="BackupController" />'s own <c>backup:read</c>/<c>backup:write</c> pair for
///         reasoning of a similar shape) -- so an operator can grant a role broad ordinary write
///         access across this server's configuration (zones, records, filter lists, and so on)
///         without that same grant silently also handing out the ability to take real DNS
///         resolution offline. <see cref="List" /> itself only reveals which services exist and
///         whether each is currently running -- no secret or configuration data -- so it is gated
///         by the lighter <c>services:read</c> instead, the same "read reveals nothing dangerous"
///         reasoning <see cref="PermissionsController" />'s own doc comment already applies
///         elsewhere.
///     </para>
/// </summary>
/// <param name="registry">The registry this controller starts, stops, and lists services through.</param>
[ApiController]
[Route("api/services")]
public class ServicesController(IManagedServiceRegistry registry) : ControllerBase
{
    /// <summary>
    ///     This action lists every service this server runs, along with whether each is currently
    ///     running -- sorted by category, then by name, for a stable, predictable presentation
    ///     order (this registry's own <see cref="IManagedServiceRegistry.Services" /> makes no
    ///     ordering guarantee of its own).
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
    ///     This action starts a single service -- binding a transport's own socket, or resuming a
    ///     background loop -- right now, in this running process. Starting a service that is
    ///     already running simply re-invokes the same underlying start logic every service in
    ///     this project was already confirmed safe to call again by a dedicated restart-safety
    ///     audit.
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
    ///     This action stops a single service -- unbinding a transport's own socket, or halting a
    ///     background loop -- right now, in this running process, leaving every other service
    ///     this server runs untouched.
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
    ///     This action restarts a single service -- stopping it, then immediately starting it
    ///     again, right now, in this running process. Exactly the sequence a genuine "reload"
    ///     request means for a service whose own <see cref="IManagedServiceRegistration.StartAsync" />
    ///     re-seeds its state fresh every call (<c>AuthoritativeZoneCache</c>/
    ///     <c>SignedZoneCache</c> reload their entire in-memory snapshot from storage on every
    ///     start, for example) -- for those two specifically, a restart is the only one of these
    ///     three actions with any externally observable effect at all, since their own
    ///     <see cref="IManagedServiceRegistration.StopAsync" /> deliberately leaves the
    ///     already-loaded snapshot in place and keeps answering from it.
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
    ///     This method looks up a registered service by name, throwing
    ///     <see cref="NotFoundException" /> when no service is registered under it.
    /// </summary>
    private IManagedServiceRegistration Require(string name) =>
        registry.Find(name) ?? throw new NotFoundException("ManagedService", name);

    /// <summary>
    ///     This method converts a registration into its REST representation.
    /// </summary>
    private static ManagedServiceSummary ToSummary(IManagedServiceRegistration service) =>
        new(service.Name, service.Category, service.IsRunning);
}
