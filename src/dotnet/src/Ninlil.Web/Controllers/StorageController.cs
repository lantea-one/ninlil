using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.Storage;
using Ninlil.Web.Models;
using Ninlil.Web.Services.Storage;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller drives a live storage-engine switch. <c>GET /api/storage</c> reports
///     which engine this host is using, and <c>POST /api/storage/switch</c> backs up the active
///     engine, provisions and migrates a target engine, moves the running process onto it and
///     restores the backup into it, with no restart (see
///     <see cref="StorageEngineSwitchService" />). <see cref="IStorageEngineSwitchService" />
///     lists which engines can take part; MongoDB cannot.
///     <para>
///         Like <see cref="ServicesController" />, this is one of the most dangerous endpoints in
///         the API. An unreachable target fails safely: the switch is rolled back and the old
///         engine keeps serving. A target that connects but is the wrong database (a production
///         database mistaken for a scratch one) is overwritten with this server's data, as a
///         manual <see cref="BackupController.Restore" /> would. The switch therefore needs the
///         separate <c>storage:manage</c> permission, so a broad configuration-write grant does
///         not also allow moving or overwriting where all the data lives. <see cref="Status" />
///         reveals no connection string or credential, so it needs only <c>storage:read</c>.
///     </para>
/// </summary>
/// <param name="switchService">The service that reports status and performs the switch.</param>
[ApiController]
[Route("api/storage")]
public class StorageController(IStorageEngineSwitchService switchService) : ControllerBase
{
    /// <summary>
    ///     This action reports the active storage engine, whether a switch is in progress, and
    ///     which engines a switch can target. Requires <c>storage:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("storage:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(StorageStatusExample))]
    public ActionResult<StorageStatus> Status() => Ok(switchService.GetStatus());

    /// <summary>
    ///     This action switches the storage engine in-process: it backs up the active engine,
    ///     provisions and migrates the target, moves the running server onto it and restores the
    ///     backup into it. It returns once every row is copied and the server is using the new
    ///     engine, which at homelab or small-office scale takes well under a minute. Requires
    ///     <c>storage:manage</c>.
    ///     <para>
    ///         The request carries per-engine connection fields (a file path for SQLite; host,
    ///         port, database, username, password and encryption mode for the others) rather than a
    ///         free-text connection string. The server builds the connection string with each
    ///         engine's <c>*ConnectionStringBuilder</c>. A failed switch returns 400 and leaves the
    ///         previous engine active with its data untouched; a switch already in progress returns
    ///         409.
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
