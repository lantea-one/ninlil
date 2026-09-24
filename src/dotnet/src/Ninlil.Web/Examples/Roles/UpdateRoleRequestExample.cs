using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UpdateRoleRequest" />, referenced by
///     <see cref="Controllers.RolesController.Update" />.
/// </summary>
public class UpdateRoleRequestExample : IExamplesProvider<UpdateRoleRequest>
{
    /// <summary>
    ///     This method returns a revised, more specific description for an existing role.
    /// </summary>
    public UpdateRoleRequest GetExamples() =>
        new(
            "Can manage zones, records, forwarders, and DNS rewrites. Cannot manage users, roles, or server settings."
        );
}
