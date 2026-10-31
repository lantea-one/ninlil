using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Web.Examples.Backup;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller creates and restores full-system backup archives through
///     <see cref="IBackupService" />: the database (apart from history tables), the Data
///     Protection key ring, the shared TLS certificate and the runtime configuration file.
///     Creating a backup needs <c>backup:read</c>, because it reads and exports the whole system
///     (the verb is POST only because the passphrase travels in a body). Restoring needs
///     <c>backup:write</c>, since it wipes and replaces all stored data, the most destructive
///     write in the API.
///     <para>
///         <see cref="BackupDecryptionFailedException" /> and
///         <see cref="BackupArchiveInvalidException" /> stay as <c>Server.Backup</c> defines
///         them, since they carry safe messages and its tests assert on those types. This
///         controller translates them, and the service's <see cref="ArgumentException" />, into a
///         <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" /> (400) with the same
///         message, as <c>RecordsController.TranslateZoneUpdateFailure</c> does for
///         <c>IZoneUpdateService</c>. A <see cref="BackupRestoreInProgressException" /> becomes a
///         <see cref="ConflictException" /> (409) here, and only here: the storage engine switch
///         also restores through the service and must see the original exception.
///     </para>
/// </summary>
/// <param name="backupService">Creates and restores archives.</param>
/// <param name="logger">Records rejected restore attempts.</param>
/// <param name="accessTokenRevalidator">
///     Told after a restore, so access tokens already issued are checked against the restored
///     accounts, roles and security stamps on their next request and not after the cache lifetime.
/// </param>
[ApiController]
[Route("api/backup")]
public class BackupController(
    IBackupService backupService,
    ILogger<BackupController> logger,
    IAccessTokenRevalidator accessTokenRevalidator
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the largest request body, in bytes, either action accepts: 2 GiB,
    ///     well above Kestrel's default limit, which <c>[RequestSizeLimit]</c> overrides for these
    ///     actions only. The whole archive is held in memory while it is created or restored, which
    ///     is acceptable at homelab and small-office scale.
    /// </summary>
    private const long MaximumArchiveSizeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    ///     This action builds a complete backup archive and returns it as a file download
    ///     (<c>application/octet-stream</c>, <c>ninlil-backup-*.ltbk</c>), not JSON. With
    ///     <see cref="BackupCreateRequest.Passphrase" /> the archive is encrypted with AES-256-GCM;
    ///     without one it is unencrypted and marked as such in its header. Returns 400 when the
    ///     passphrase is only whitespace or too short. Requires <c>backup:read</c>.
    /// </summary>
    /// <param name="request">The passphrase to encrypt the archive with; omit it for no encryption.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost]
    [RequirePermission("backup:read")]
    [RequestSizeLimit(MaximumArchiveSizeBytes)]
    [SwaggerRequestExample(typeof(BackupCreateRequest), typeof(BackupCreateRequestExample))]
    public async Task<IActionResult> Create(
        [FromBody] BackupCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        try
        {
            byte[] archive = await backupService.CreateBackupAsync(
                request?.Passphrase,
                stoppingToken
            );

            string fileName = $"ninlil-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.ltbk";
            return File(archive, "application/octet-stream", fileName);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException(exception.Message);
        }
    }

    /// <summary>
    ///     This action restores the database, Data Protection key ring, TLS certificate and runtime
    ///     configuration file from an archive, encrypted or not. All stored data is wiped and
    ///     replaced; there is no partial or merge restore. The archive is decrypted, checked and
    ///     parsed before anything is written, so a wrong or missing passphrase or a corrupted,
    ///     tampered or unrecognized archive returns 400 and leaves the server's data untouched.
    ///     Returns 409 when another restore is already running (nothing is changed; try again when
    ///     it has finished). On a relational storage engine the wipe and import are one
    ///     transaction, so a database failure also leaves the data as it was. The server must
    ///     be restarted afterwards to pick up the restored key ring, certificate and settings.
    ///     Requires <c>backup:write</c>.
    /// </summary>
    /// <param name="request">
    ///     The archive to restore and the passphrase it was encrypted with; leave the passphrase
    ///     empty for an unencrypted archive.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("restore")]
    [RequirePermission("backup:write")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumArchiveSizeBytes)]
    [SwaggerRequestExample(typeof(BackupRestoreRequest), typeof(BackupRestoreRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BackupRestoreResponseExample))]
    public async Task<ActionResult<BackupRestoreResponse>> Restore(
        [FromForm] BackupRestoreRequest request,
        CancellationToken stoppingToken
    )
    {
        if (request?.Archive is null || request.Archive.Length == 0)
            throw new ValidationException("An archive file is required.");

        try
        {
            await using Stream archiveStream = request.Archive.OpenReadStream();
            BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
                archiveStream,
                request.Passphrase,
                stoppingToken
            );

            return Ok(
                new BackupRestoreResponse(
                    summary.TotalEntitiesImported,
                    summary.EntityCounts,
                    BackupRestoreResponse.RestartRequiredNote
                )
            );
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException(exception.Message);
        }
        catch (BackupRestoreInProgressException exception)
        {
            logger.LogWarning("Rejected a restore attempt: {Reason}", exception.Message);
            throw new ConflictException(exception.Message);
        }
        catch (BackupDecryptionFailedException exception)
        {
            logger.LogWarning("Rejected a restore attempt: {Reason}", exception.Message);
            throw new ValidationException(exception.Message);
        }
        catch (BackupArchiveInvalidException exception)
        {
            logger.LogWarning("Rejected a restore attempt: {Reason}", exception.Message);
            throw new ValidationException(exception.Message);
        }
        finally
        {
            // The restore replaces the user, role and security-stamp rows under the cache, and
            // a failure part-way may have replaced some of them.
            accessTokenRevalidator.Invalidate();
        }
    }
}
