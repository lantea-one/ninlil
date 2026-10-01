using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the Swagger example for <see cref="UpdateRoleRequest" />, the request
///     body of <see cref="Controllers.RolesController.Update" />.
/// </summary>
public class UpdateRoleRequestExample : IExamplesProvider<UpdateRoleRequest>
{
    /// <summary>
    ///     This method returns a revised description for an existing role.
    /// </summary>
    public UpdateRoleRequest GetExamples() =>
        new(
            "Can manage zones, records, forwarders, and DNS rewrites. Cannot manage users, roles, or server settings."
        );
}
