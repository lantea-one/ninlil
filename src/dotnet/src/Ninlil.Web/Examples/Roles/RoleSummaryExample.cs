using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the Swagger example for one <see cref="RoleSummary" />, as returned by
///     <see cref="Controllers.RolesController.Get" /> and
///     <see cref="Controllers.RolesController.Create" />.
/// </summary>
public class RoleSummaryExample : IExamplesProvider<RoleSummary>
{
    /// <summary>
    ///     This method returns the role from <see cref="CreateRoleRequestExample" /> with
    ///     permissions for zones (which covers records) and forwarders, and nothing more.
    /// </summary>
    public RoleSummary GetExamples() =>
        new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "dns-operator",
            "Can manage zones, records, and forwarders. Cannot manage users, roles, or server settings.",
            ["zones:read", "zones:write", "forwarders:read", "forwarders:write"]
        );
}
