using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BackupRestoreRequest" />, referenced by
///     <see cref="Controllers.BackupController.Restore" />. This request is a
///     <c>multipart/form-data</c> body, not JSON, so <see cref="BackupRestoreRequest.Archive" />
///     itself -- the uploaded file part -- has no meaningful example value to render here; only
///     <see cref="BackupRestoreRequest.Passphrase" /> is shown.
/// </summary>
public class BackupRestoreRequestExample : IExamplesProvider<BackupRestoreRequest>
{
    /// <summary>
    ///     This method returns the same obviously-illustrative passphrase
    ///     <see cref="BackupCreateRequestExample" /> already uses, since restoring an archive
    ///     always means supplying the exact passphrase it was originally encrypted with.
    /// </summary>
    public BackupRestoreRequest GetExamples() =>
        new() { Passphrase = "CorrectHorseBatteryStaple1!" };
}
