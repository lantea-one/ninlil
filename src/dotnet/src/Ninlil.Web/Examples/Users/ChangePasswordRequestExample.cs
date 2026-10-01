using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="ChangePasswordRequest" />, the
///     request body of <see cref="Controllers.UsersController.ChangeMyPassword" />.
/// </summary>
public class ChangePasswordRequestExample : IExamplesProvider<ChangePasswordRequest>
{
    /// <summary>
    ///     This method returns a self-service password change. Both passwords are examples the
    ///     caller chooses, not server secrets, and the new one meets the default password policy.
    /// </summary>
    public ChangePasswordRequest GetExamples() =>
        new("CorrectHorseBatteryStaple1!", "EvenStrongerPassphrase2!");
}
