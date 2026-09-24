using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UserRoleAssignmentRequest" />, referenced by
///     <see cref="Controllers.UsersController.AddToRole" />.
/// </summary>
public class UserRoleAssignmentRequestExample : IExamplesProvider<UserRoleAssignmentRequest>
{
    /// <summary>
    ///     This method returns a request naming a realistic, already-illustrated role -- the same
    ///     one <see cref="Roles.CreateRoleRequestExample" /> creates.
    /// </summary>
    public UserRoleAssignmentRequest GetExamples() => new("dns-operator");
}
