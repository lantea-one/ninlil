using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="BackupCreateRequest" />, referenced by
///     <see cref="Controllers.BackupController.Create" />.
/// </summary>
public class BackupCreateRequestExample : IExamplesProvider<BackupCreateRequest>
{
    /// <summary>
    ///     This method returns a plain, obviously-illustrative passphrase -- a caller-supplied
    ///     value the caller already knows and chose themselves, not a server-generated secret
    ///     this project needs to keep out of documentation, the same reasoning
    ///     <c>LoginRequestExample</c>'s own password value already documents.
    /// </summary>
    public BackupCreateRequest GetExamples() => new("CorrectHorseBatteryStaple1!");
}
