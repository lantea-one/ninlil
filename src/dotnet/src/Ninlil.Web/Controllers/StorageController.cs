using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.Storage;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this host's own live storage-engine switch:
///     <c>GET /api/storage</c> reports which engine this host is currently using, and
///     <c>POST /api/storage/switch</c> backs up the currently active engine, provisions and
///     migrates a target engine, flips this process's own live storage onto it, and restores the
///     backup into it -- all within this same running process, no restart involved (see
///     <see cref="StorageEngineSwitchService" />'s own doc comment for exactly how). See
///     <see cref="IStorageEngineSwitchService" />'s own doc comment for the four relational
///     engines this covers and why MongoDB is not among them yet.
///     <para>
///         <b>This is, alongside <see cref="ServicesController" />, one of the most operationally
///         dangerous endpoints this REST API exposes</b> -- a wrong or unreachable target
///         connection string fails safely (the switch is rolled back, the previously active
///         engine keeps serving every request, per <see cref="IStorageEngineSwitchService" />'s
///         own doc comment), but a target that <em>does</em> connect successfully yet is not what
///         the operator actually intended (a production database mistaken for a scratch one, for
///         example) is wiped and overwritten with this server's own data, exactly like a manual
///         <see cref="BackupController.Restore" /> already can be. Both write actions here are
///         therefore gated by the same deliberately separate, narrower <c>storage:manage</c>
///         permission <see cref="ServicesController" />'s own <c>services:manage</c> already
///         establishes the precedent for -- an operator can be granted this server's usual broad
///         configuration write access without that same grant silently also handing out the
///         ability to relocate or overwrite where this server's entire configuration and DNS data
///         lives. <see cref="Status" /> itself reveals only which engine is active and whether a
///         switch is in progress -- no connection string, no credential -- so it is gated by the
///         lighter <c>storage:read</c> instead.
///     </para>
/// </summary>
/// <param name="switchService">The service this controller reports status through and drives a switch with.</param>
[ApiController]
[Route("api/storage")]
public class StorageController(IStorageEngineSwitchService switchService) : ControllerBase
{
    /// <summary>
    ///     This action reports this host's own currently active storage engine, whether a switch
    ///     is in progress right now, and which engines a live switch can target.
    /// </summary>
    [HttpGet]
    [RequirePermission("storage:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(StorageStatusExample))]
    public ActionResult<StorageStatus> Status() => Ok(switchService.GetStatus());

    /// <summary>
    ///     This action performs a real, in-process storage-engine switch: backs up this host's
    ///     currently active engine, provisions and migrates <paramref name="request" />'s own
    ///     target engine, flips this process's own live storage onto it, and restores the backup
    ///     into it -- returning once every row has been carried across and this process is
    ///     already serving from the new engine. This call blocks for the whole duration of the
    ///     switch (a real backup-and-restore pass, plus a migration), which, at this project's own
    ///     stated homelab/small-org scale, is expected to complete in well under a minute.
    ///     <para>
    ///         <paramref name="request" /> carries structured, per-engine connection fields (a
    ///         file path for SQLite; host/port/database name/username/password/encryption mode for
    ///         the other three) rather than a single free-text connection string, mirroring
    ///         <see cref="EventBrokerSettingsController" />'s own structured-field pattern. This
    ///         action assembles the real ADO.NET connection string via
    ///         <see cref="StorageConnectionStringAssembler" /> -- always through the target
    ///         engine's own official <c>*ConnectionStringBuilder</c> type, never by hand-formatting
    ///         a string -- before ever calling <see cref="IStorageEngineSwitchService" />, which
    ///         itself still operates on that single already-assembled connection string, unaware
    ///         structured fields exist at all.
    ///     </para>
    /// </summary>
    /// <param name="request">Which engine to switch to, and its own structured connection fields.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("switch")]
    [RequirePermission("storage:manage")]
    [SwaggerRequestExample(
        typeof(StorageEngineSwitchApiRequest),
        typeof(StorageEngineSwitchApiRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(StorageEngineSwitchResultExample))]
    public async Task<ActionResult<StorageEngineSwitchResult>> Switch(
        [FromBody] StorageEngineSwitchApiRequest request,
        CancellationToken stoppingToken
    )
    {
        string connectionString = StorageConnectionStringAssembler.Assemble(request);

        StorageEngineSwitchResult result = await switchService.SwitchEngineAsync(
            new StorageEngineSwitchRequest(request.TargetEngine, connectionString),
            stoppingToken
        );

        return Ok(result);
    }
}
