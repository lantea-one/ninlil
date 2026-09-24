using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Roles;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="RoleSummary" />, referenced by <see cref="Controllers.RolesController.List" />.
/// </summary>
public class RoleSummaryPagedResultExample : IExamplesProvider<PagedResult<RoleSummary>>
{
    /// <summary>
    ///     This method returns one page of two representative roles: a DNS-scoped operator role
    ///     and a read-only auditor role, in the exact envelope a real list response carries.
    /// </summary>
    public PagedResult<RoleSummary> GetExamples() =>
        new(
            [
                new RoleSummary(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    "dns-operator",
                    "Can manage zones, records, and forwarders. Cannot manage users, roles, or server settings.",
                    ["zones:read", "zones:write", "forwarders:read", "forwarders:write"]
                ),
                new RoleSummary(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    "auditor",
                    "Can view every read-only resource. Cannot change anything.",
                    ["zones:read", "querylog:read", "status:read"]
                ),
            ],
            0,
            50,
            2
        );
}
