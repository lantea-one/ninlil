using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="ChangeEmailRequest" />, the request
///     body of <see cref="Controllers.UsersController.ChangeMyEmail" />.
/// </summary>
public class ChangeEmailRequestExample : IExamplesProvider<ChangeEmailRequest>
{
    /// <summary>
    ///     This method returns a self-service address change. The password is an example the
    ///     caller chooses, not a server secret.
    /// </summary>
    public ChangeEmailRequest GetExamples() =>
        new("jamie.smith@example.com", "CorrectHorseBatteryStaple1!");
}
