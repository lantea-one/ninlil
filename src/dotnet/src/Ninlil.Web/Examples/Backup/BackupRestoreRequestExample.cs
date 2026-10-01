using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the Swagger example for <see cref="BackupRestoreRequest" />, the form
///     body of <see cref="Controllers.BackupController.Restore" />. The body is
///     <c>multipart/form-data</c>, so the uploaded <see cref="BackupRestoreRequest.Archive" /> has
///     no example value; only <see cref="BackupRestoreRequest.Passphrase" /> is shown.
/// </summary>
public class BackupRestoreRequestExample : IExamplesProvider<BackupRestoreRequest>
{
    /// <summary>
    ///     This method returns the passphrase <see cref="BackupCreateRequestExample" /> uses, since
    ///     a restore needs the passphrase the archive was encrypted with.
    /// </summary>
    public BackupRestoreRequest GetExamples() =>
        new() { Passphrase = "CorrectHorseBatteryStaple1!" };
}
