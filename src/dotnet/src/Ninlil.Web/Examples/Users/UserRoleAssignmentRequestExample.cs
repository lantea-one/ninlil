using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="UserRoleAssignmentRequest" />, the
///     request body of <see cref="Controllers.UsersController.AddToRole" />.
/// </summary>
public class UserRoleAssignmentRequestExample : IExamplesProvider<UserRoleAssignmentRequest>
{
    /// <summary>
    ///     This method returns the role <see cref="Roles.CreateRoleRequestExample" /> creates.
    /// </summary>
    public UserRoleAssignmentRequest GetExamples() => new("dns-operator");
}
