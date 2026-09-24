using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RolePermissionRequest" />, referenced by
///     <see cref="Controllers.RolesController.AddPermission" /> and
///     <see cref="Controllers.RolesController.RemovePermission" />.
/// </summary>
public class RolePermissionRequestExample : IExamplesProvider<RolePermissionRequest>
{
    /// <summary>
    ///     This method returns a request naming one of this project's own real,
    ///     already-registered permission strings, matching
    ///     <see cref="RoleSummaryExample" />'s own catalog.
    /// </summary>
    public RolePermissionRequest GetExamples() => new("zones:write");
}
