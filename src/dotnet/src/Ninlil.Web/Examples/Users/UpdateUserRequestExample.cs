using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="UpdateUserRequest" />, the request
///     body of <see cref="Controllers.UsersController.Update" />.
/// </summary>
public class UpdateUserRequestExample : IExamplesProvider<UpdateUserRequest>
{
    /// <summary>
    ///     This method returns a change of e-mail address that keeps the account enabled.
    /// </summary>
    public UpdateUserRequest GetExamples() => new("Jamie Smith", "jamie.smith@example.com", false);
}
