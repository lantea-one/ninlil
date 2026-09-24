using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a single
///     <see cref="RoleSummary" />, referenced by <see cref="Controllers.RolesController.Get" />
///     and <see cref="Controllers.RolesController.Create" />.
/// </summary>
public class RoleSummaryExample : IExamplesProvider<RoleSummary>
{
    /// <summary>
    ///     This method returns a role granting exactly the permissions
    ///     <see cref="CreateRoleRequestExample" />'s own description promises, no more.
    /// </summary>
    public RoleSummary GetExamples() =>
        new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "dns-operator",
            "Can manage zones, records, and forwarders. Cannot manage users, roles, or server settings.",
            ["zones:read", "zones:write", "forwarders:read", "forwarders:write"]
        );
}
