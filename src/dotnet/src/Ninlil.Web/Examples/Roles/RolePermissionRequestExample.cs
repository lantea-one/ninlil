using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the Swagger example for <see cref="RolePermissionRequest" />, the
///     request body of <see cref="Controllers.RolesController.AddPermission" /> and
///     <see cref="Controllers.RolesController.RemovePermission" />.
/// </summary>
public class RolePermissionRequestExample : IExamplesProvider<RolePermissionRequest>
{
    /// <summary>
    ///     This method returns a request naming a registered permission, one the role in
    ///     <see cref="RoleSummaryExample" /> holds.
    /// </summary>
    public RolePermissionRequest GetExamples() => new("zones:write");
}
