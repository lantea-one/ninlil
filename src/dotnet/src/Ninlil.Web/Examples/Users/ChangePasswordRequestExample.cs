using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ChangePasswordRequest" />, referenced by
///     <see cref="Controllers.UsersController.ChangeMyPassword" />.
/// </summary>
public class ChangePasswordRequestExample : IExamplesProvider<ChangePasswordRequest>
{
    /// <summary>
    ///     This method returns one realistic self-service password change. Both values are
    ///     plain, obviously-illustrative examples -- caller-supplied credentials the caller
    ///     already knows, not server-generated secrets this project needs to keep out of
    ///     documentation.
    /// </summary>
    public ChangePasswordRequest GetExamples() =>
        new("CorrectHorseBatteryStaple1!", "EvenStrongerPassphrase2!");
}
