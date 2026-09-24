using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Users;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UserPermissionRequest" />, referenced by
///     <see cref="Controllers.UsersController.AddPermission" /> and
///     <see cref="Controllers.UsersController.RemovePermission" />.
/// </summary>
public class UserPermissionRequestExample : IExamplesProvider<UserPermissionRequest>
{
    /// <summary>
    ///     This method returns a request naming one of this project's own real,
    ///     already-registered permission strings, exactly matching
    ///     <see cref="Permissions.PermissionListExample" />'s own catalog.
    /// </summary>
    public UserPermissionRequest GetExamples() => new("backup:read");
}
