using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpScopeStatisticsDetail" />, referenced by
///     <see cref="Controllers.DhcpScopesController.Statistics" />.
/// </summary>
public class DhcpScopeStatisticsDetailExample : IExamplesProvider<DhcpScopeStatisticsDetail>
{
    /// <summary>
    ///     This method returns the same home-LAN scope <see cref="DhcpScopeDetailExample" /> uses,
    ///     a /24 pool (101 addresses) with a handful of leases/reservations already drawn from it.
    /// </summary>
    public DhcpScopeStatisticsDetail GetExamples() =>
        new(
            Guid.Parse("a1b2c3d4-5e6f-4708-9192-a3b4c5d6e7f1"),
            "Home LAN",
            InternetProtocolVersionEnum.IPv4,
            DhcpScopeKindEnum.AddressAssignment,
            "101",
            12,
            1,
            3,
            // Not fully additive (12 + 1 + 3 != 101 - 85) since a reserved address that is also
            // currently leased is only ever counted once against the total -- see
            // DhcpScopeStatisticsDetail's own doc comment.
            "85"
        );
}
