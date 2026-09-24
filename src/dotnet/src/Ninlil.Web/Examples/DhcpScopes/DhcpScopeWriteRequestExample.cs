using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpScopeWriteRequest" />, referenced by
///     <see cref="Controllers.DhcpScopesController.Create" /> and
///     <see cref="Controllers.DhcpScopesController.Replace" />.
/// </summary>
public class DhcpScopeWriteRequestExample : IExamplesProvider<DhcpScopeWriteRequest>
{
    /// <summary>
    ///     This method returns an ordinary home-LAN IPv4 scope handing out a day-long lease by
    ///     default, up to a week at most, advertising a router and a DNS server.
    /// </summary>
    public DhcpScopeWriteRequest GetExamples() =>
        new(
            "Home LAN",
            InternetProtocolVersionEnum.IPv4,
            "192.168.1.100",
            "192.168.1.200",
            24,
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(7),
            IsDisabled: false,
            GatewayAddresses: ["192.168.1.1"],
            DnsServerAddresses: ["192.168.1.1"],
            Comments: "Main household network"
        );
}
