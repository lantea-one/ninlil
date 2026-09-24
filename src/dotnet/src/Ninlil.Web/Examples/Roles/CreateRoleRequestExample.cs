using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="CreateRoleRequest" />, referenced by
///     <see cref="Controllers.RolesController.Create" />.
/// </summary>
public class CreateRoleRequestExample : IExamplesProvider<CreateRoleRequest>
{
    /// <summary>
    ///     This method returns a role scoped to DNS operations, deliberately not identity/
    ///     administrative ones -- a realistic, least-privilege-shaped example rather than a
    ///     wildcard-holding one.
    /// </summary>
    public CreateRoleRequest GetExamples() =>
        new(
            "dns-operator",
            "Can manage zones, records, and forwarders. Cannot manage users, roles, or server settings."
        );
}
