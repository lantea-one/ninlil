using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="CreateUserRequest" />, the request
///     body of <see cref="Controllers.UsersController.Create" />.
/// </summary>
public class CreateUserRequestExample : IExamplesProvider<CreateUserRequest>
{
    /// <summary>
    ///     This method returns a new operator account. The initial password is an example chosen by
    ///     the administrator, not a server secret, and meets the default password policy.
    /// </summary>
    public CreateUserRequest GetExamples() =>
        new("jsmith", "CorrectHorseBatteryStaple1!", "jsmith@example.com", "Jamie Smith");
}
