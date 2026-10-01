using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the Swagger example for <see cref="CreateRoleRequest" />, the request
///     body of <see cref="Controllers.RolesController.Create" />.
/// </summary>
public class CreateRoleRequestExample : IExamplesProvider<CreateRoleRequest>
{
    /// <summary>
    ///     This method returns a least-privilege role for DNS operations only, with no identity or
    ///     administration rights.
    /// </summary>
    public CreateRoleRequest GetExamples() =>
        new(
            "dns-operator",
            "Can manage zones, records, and forwarders. Cannot manage users, roles, or server settings."
        );
}
