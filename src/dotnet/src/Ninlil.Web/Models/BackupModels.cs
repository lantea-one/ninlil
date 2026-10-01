using Microsoft.AspNetCore.Http;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the request body for <see cref="Controllers.BackupController.Create" />.
/// </summary>
/// <param name="Passphrase">
///     The passphrase to encrypt the archive with, or null or blank for an unencrypted archive
///     whose header marks it as such (no weak or derived default key is used). The server never
///     stores the passphrase, so losing it means the archive can never be decrypted.
/// </param>
public sealed record BackupCreateRequest(string Passphrase);

/// <summary>
///     This class is the multipart form body for <see cref="Controllers.BackupController.Restore" />.
///     It is a class rather than a positional record because the <c>[FromForm]</c> model binder
///     needs a settable property for an uploaded <see cref="IFormFile" />.
/// </summary>
public sealed class BackupRestoreRequest
{
    /// <summary>
    ///     This property contains the backup archive to restore from, encrypted or not.
    /// </summary>
    public IFormFile Archive { get; set; }

    /// <summary>
    ///     This property contains the passphrase <see cref="Archive" /> was encrypted with, or null
    ///     or blank for an unencrypted archive. Whether the archive is encrypted is read from its
    ///     header, not inferred from this field.
    /// </summary>
    public string Passphrase { get; set; }
}

/// <summary>
///     This record is the success response body for
///     <see cref="Controllers.BackupController.Restore" />.
///     <see cref="Controllers.BackupController.Create" /> has no JSON response: it returns the
///     archive itself as a file download.
/// </summary>
/// <param name="TotalEntitiesImported">The total number of rows imported across every entity type.</param>
/// <param name="EntityCounts">How many rows of each entity type were imported, by name.</param>
/// <param name="Note">
///     Always <see cref="RestartRequiredNote" />: the restored database connection, Data
///     Protection key ring and TLS certificate take effect only after a restart.
/// </param>
public sealed record BackupRestoreResponse(
    int TotalEntitiesImported,
    IReadOnlyDictionary<string, int> EntityCounts,
    string Note
)
{
    /// <summary>
    ///     This constant contains the note every successful restore returns, telling the operator to
    ///     restart the process.
    /// </summary>
    public const string RestartRequiredNote =
        "Restore succeeded. Restart this process now so the restored database connection, Data "
        + "Protection key ring, and TLS certificate are actually picked up -- none of them take "
        + "effect for this already-running process on their own.";
}
