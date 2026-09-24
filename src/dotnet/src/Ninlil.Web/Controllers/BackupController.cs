using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Web.Examples.Backup;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller creates and restores a full-system, passphrase-encrypted backup archive
///     via <see cref="IBackupService" /> -- every entity this server persists, the Data
///     Protection key ring, the shared TLS certificate, and the runtime configuration file,
///     bundled together. This is a flat, top-level, non-nested resource, and, unlike every other
///     resource this project's REST API exposes, is deliberately gated by two <em>different</em>
///     permission claims across its own two actions rather than one <c>backup:read</c>/
///     <c>backup:write</c> pair applied the usual "read the resource" versus "mutate the
///     resource" way: creating a backup is gated by <c>backup:read</c> (it fundamentally reads
///     and exports the entire system, the same "GET-shaped" reasoning behind every other
///     controller's own <c>:read</c> claim, even though the actual HTTP verb here is
///     <c>POST</c>, since a body -- the passphrase -- is required); restoring one is gated by
///     <c>backup:write</c> (the single most destructive write action this REST API exposes,
///     wiping and replacing every entity this server persists).
///     <para>
///         <see cref="IBackupService" />'s own dedicated failure types --
///         <see cref="BackupDecryptionFailedException" />/<see cref="BackupArchiveInvalidException" /> --
///         are deliberately left completely untouched, not folded into or replaced by this
///         project's own <c>NotFoundException</c>/<c>ConflictException</c>/<c>ValidationException</c>
///         hierarchy: they already carry a clear, safe-to-return message and a name specific to
///         the exact failure mode, and <c>Server.Backup</c>'s own permanent test suite already
///         asserts against their exact concrete types directly. This controller instead catches
///         each one here, at the REST boundary, and translates it into a
///         <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" /> carrying the exact
///         same message -- the same "leave a shared/independently-owned exception type alone,
///         translate only where a REST response is actually produced" precedent
///         <c>RecordsController</c>'s own <c>TranslateZoneUpdateFailure</c> helper already
///         established for <c>IZoneUpdateService</c>'s own <see cref="InvalidOperationException" />.
///     </para>
/// </summary>
/// <param name="backupService">The service this controller creates and restores an archive through.</param>
/// <param name="logger">The logger this controller records a rejected restore attempt to.</param>
[ApiController]
[Route("api/backup")]
public class BackupController(IBackupService backupService, ILogger<BackupController> logger)
    : ControllerBase
{
    /// <summary>
    ///     This constant contains the maximum size, in bytes, of an uploaded restore archive this
    ///     action accepts -- 2 gibibytes, generously sized for this project's own stated homelab/
    ///     small-org target scale (the entire archive is buffered in memory during both creation
    ///     and restoration, an accepted limitation at that scale, the same "in-memory, not
    ///     streaming" tradeoff this project already accepts for its own whole-table-scan and
    ///     in-memory-pagination code paths elsewhere), well above Kestrel's own default request
    ///     body size limit, which this attribute overrides for this one action only.
    /// </summary>
    private const long MaximumArchiveSizeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    ///     This action creates a complete backup archive and streams it back as a downloadable
    ///     file -- never a JSON response body, since the archive itself is the whole point of
    ///     this action. Encrypted when <see cref="BackupCreateRequest.Passphrase" /> is supplied,
    ///     or deliberately, clearly-markedly unencrypted when it is not.
    /// </summary>
    /// <param name="request">The passphrase to encrypt the archive with, or omit to skip encryption.</param>
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
    ///     This action restores this server's own full database, Data Protection key ring, TLS
    ///     certificate, and runtime configuration file from a previously created archive, of
    ///     either kind -- encrypted or, per its own envelope header, deliberately unencrypted.
    ///     Every entity currently persisted is wiped and replaced -- there is no partial or merge
    ///     restore. The archive is fully decrypted (if encrypted) and structurally validated
    ///     before any of that happens, so a rejected request (wrong or missing passphrase,
    ///     corrupted or tampered archive, unrecognized format) is guaranteed to have left this
    ///     server's own data completely untouched.
    /// </summary>
    /// <param name="request">
    ///     The archive to restore from, and the passphrase it was encrypted with -- left
    ///     null/blank when restoring an archive that was itself created without one.
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
    }
}
