using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.Storage;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller schedules a storage-engine switch. <c>GET /api/storage</c> reports which
///     engine this host uses and any switch that is waiting. <c>POST /api/storage/switch</c>
///     checks the target database and saves the switch; <c>DELETE /api/storage/switch</c>
///     cancels it. The data is copied at the next start by
///     <see cref="PendingStorageSwitchApplier" />, before any background service runs, so no
///     write is lost. <see cref="IStorageEngineSwitchService" /> lists which engines can take
///     part; MongoDB cannot.
///     <para>
///         Like <see cref="ServicesController" />, this is one of the most dangerous endpoints in
///         the API. A target that connects but is the wrong database (a production database
///         mistaken for a scratch one) has its Ninlil data replaced with this server's data at
///         the next start, as a manual <see cref="BackupController.Restore" /> would; the answer
///         says when the target already holds data. The switch therefore needs the separate
///         <c>storage:manage</c> permission, so a broad configuration-write grant does not also
///         allow moving or overwriting where all the data lives. <see cref="Status" /> reveals no
///         connection string or credential, so it needs only <c>storage:read</c>.
///     </para>
/// </summary>
/// <param name="switchService">The service that reports status and schedules the switch.</param>
[ApiController]
[Route("api/storage")]
public class StorageController(IStorageEngineSwitchService switchService) : ControllerBase
{
    /// <summary>
    ///     This action reports the active storage engine, the switch waiting for the next start,
    ///     the outcome of a switch this start applied, and which engines a switch can target. It
    ///     also lists the primary's hosts and, when a read replica is configured, whether the
    ///     replica serves reads now (lag, schema, reason) and how many database scopes went to
    ///     each side. Host names and ports are the only connection details it returns.
    ///     Requires <c>storage:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("storage:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(StorageStatusExample))]
    public ActionResult<StorageStatus> Status() => Ok(switchService.GetStatus());

    /// <summary>
    ///     This action checks the target database and saves a switch to it, which the next start
    ///     applies. It returns 202 at once: nothing is copied and the server keeps its engine.
    ///     Requires <c>storage:manage</c>.
    ///     <para>
    ///         The request carries per-engine connection fields (a file path for SQLite; host,
    ///         port, database, username, password and encryption mode for the others) rather than a
    ///         free-text connection string. The server builds the connection string with each
    ///         engine's <c>*ConnectionStringBuilder</c>. An unreachable target, a target that a
    ///         newer Ninlil upgraded, a request naming MongoDB, or a request made while a read replica
    ///         or cluster setting of the engine in use is configured returns 400 and saves nothing. A
    ///         second request replaces the first. The answer says whether Ninlil points the
    ///         runtime configuration file at the target itself, or the operator must first set
    ///         the storage environment variables.
    ///     </para>
    /// </summary>
    /// <param name="request">The engine to switch to and its connection fields.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("switch")]
    [RequirePermission("storage:manage")]
    [SwaggerRequestExample(
        typeof(StorageEngineSwitchApiRequest),
        typeof(StorageEngineSwitchApiRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status202Accepted, typeof(StorageSwitchScheduledExample))]
    public async Task<ActionResult<StorageSwitchScheduled>> Switch(
        [FromBody] StorageEngineSwitchApiRequest request,
        CancellationToken stoppingToken
    )
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(request);

        StorageSwitchScheduled scheduled = await switchService.ScheduleSwitchAsync(
            new StorageEngineSwitchRequest(request.TargetEngine, connectionString),
            User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "unknown",
            stoppingToken
        );

        return Accepted(scheduled);
    }

    /// <summary>
    ///     This action cancels the switch waiting for the next start. Returns 204, or 404 when
    ///     none is waiting. Requires <c>storage:manage</c>.
    /// </summary>
    [HttpDelete("switch")]
    [RequirePermission("storage:manage")]
    public IActionResult CancelSwitch() =>
        switchService.CancelSwitch()
            ? NoContent()
            : throw new NotFoundException("Pending storage switch", "storage-switch");
}
