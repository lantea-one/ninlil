using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CreateUserRequest" />, referenced by
///     <see cref="Controllers.UsersController.Create" />.
/// </summary>
public class CreateUserRequestExample : IExamplesProvider<CreateUserRequest>
{
    /// <summary>
    ///     This method returns a new operator account. The password value is a plain,
    ///     illustrative example -- it is an administrator-supplied initial credential, not a
    ///     server-generated secret this project needs to keep out of documentation.
    /// </summary>
    public CreateUserRequest GetExamples() =>
        new("jsmith", "CorrectHorseBatteryStaple1!", "jsmith@example.com", "Jamie Smith");
}
