using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpScopeStatisticsDetail" />, as
///     returned by <see cref="Controllers.DhcpScopesController.Statistics" />.
/// </summary>
public class DhcpScopeStatisticsDetailExample : IExamplesProvider<DhcpScopeStatisticsDetail>
{
    /// <summary>
    ///     This method returns the "Home LAN" scope from <see cref="DhcpScopeDetailExample" />: its
    ///     100 to 200 range holds 101 addresses, of which 12 are leased, 1 offered and 3 reserved.
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
            // Here 12 + 1 + 3 = 101 - 85. A reserved address that is also leased is counted once
            // against the total, so in general the three counts can add up to more than the
            // occupied slots (see DhcpScopeStatisticsDetail).
            "85"
        );
}
