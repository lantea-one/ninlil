using Microsoft.AspNetCore.Http;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the request body for <see cref="Controllers.BackupController.Create" />.
/// </summary>
/// <param name="Passphrase">
///     The passphrase to encrypt the resulting archive with, or null/blank to deliberately skip
///     encryption and produce a clearly-marked unencrypted archive instead -- an honest, explicit
///     choice, not a weak or default-derived key. When supplied, this server never stores it
///     anywhere -- losing it means losing the ability to ever decrypt this specific archive
///     again.
/// </param>
public sealed record BackupCreateRequest(string Passphrase);

/// <summary>
///     This class is the multipart form body for <see cref="Controllers.BackupController.Restore" />
///     -- a plain class, not a record, since ASP.NET Core's own <c>[FromForm]</c> model binder
///     needs a settable property for the uploaded file, which a record's own positional
///     constructor parameter cannot represent for an <see cref="IFormFile" />.
/// </summary>
public sealed class BackupRestoreRequest
{
    /// <summary>
    ///     This property contains the backup archive to restore from, encrypted or not.
    /// </summary>
    public IFormFile Archive { get; set; }

    /// <summary>
    ///     This property contains the passphrase <see cref="Archive" /> was encrypted with, if it
    ///     is an encrypted archive -- left null/blank when restoring an archive that was itself
    ///     created without one. Which case applies is detected from the archive's own contents,
    ///     not decided by whether this field is populated.
    /// </summary>
    public string Passphrase { get; set; }
}

/// <summary>
///     This record is the success response body for
///     <see cref="Controllers.BackupController.Restore" />.
///     <see cref="Controllers.BackupController.Create" />'s own successful response, by contrast,
///     streams the encrypted archive itself as the response body -- never this or any other JSON
///     wrapper around it.
/// </summary>
/// <param name="TotalEntitiesImported">The total number of rows imported across every entity type.</param>
/// <param name="EntityCounts">How many rows of each entity type were imported, by name.</param>
/// <param name="Note">
///     A note explaining that a restart is required before the restored database connection,
///     Data Protection key ring, and TLS certificate actually take effect -- this project's own
///     established convention for every runtime-settings write, extended here to the single most
///     consequential write this REST API can ever perform.
/// </param>
public sealed record BackupRestoreResponse(
    int TotalEntitiesImported,
    IReadOnlyDictionary<string, int> EntityCounts,
    string Note
)
{
    /// <summary>
    ///     This constant contains the exact wording every runtime-settings write action in this
    ///     project already surfaces once a write takes effect only after a restart, reused here
    ///     verbatim rather than inventing a second, slightly different note for the same fact.
    /// </summary>
    public const string RestartRequiredNote =
        "Restore succeeded. Restart this process now so the restored database connection, Data "
        + "Protection key ring, and TLS certificate are actually picked up -- none of them take "
        + "effect for this already-running process on their own.";
}
