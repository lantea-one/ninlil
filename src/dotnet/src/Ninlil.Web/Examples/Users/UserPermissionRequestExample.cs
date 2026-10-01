using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the Swagger example for <see cref="UserPermissionRequest" />, the
///     request body of <see cref="Controllers.UsersController.AddPermission" /> and
///     <see cref="Controllers.UsersController.RemovePermission" />.
/// </summary>
public class UserPermissionRequestExample : IExamplesProvider<UserPermissionRequest>
{
    /// <summary>
    ///     This method returns a request naming a registered permission that appears in
    ///     <see cref="Permissions.PermissionListExample" />.
    /// </summary>
    public UserPermissionRequest GetExamples() => new("backup:read");
}
