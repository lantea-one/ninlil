using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Backup;

/// <summary>
///     This class supplies the Swagger example for <see cref="BackupCreateRequest" />, the request
///     body of <see cref="Controllers.BackupController.Create" />.
/// </summary>
public class BackupCreateRequestExample : IExamplesProvider<BackupCreateRequest>
{
    /// <summary>
    ///     This method returns an example passphrase, longer than the 12-character minimum. Like
    ///     the password in <c>LoginRequestExample</c>, it is chosen by the caller, not a server
    ///     secret.
    /// </summary>
    public BackupCreateRequest GetExamples() => new("CorrectHorseBatteryStaple1!");
}
