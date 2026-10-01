using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpScopeWriteRequest" />, the
///     request body of <see cref="Controllers.DhcpScopesController.Create" /> and
///     <see cref="Controllers.DhcpScopesController.Replace" />.
/// </summary>
public class DhcpScopeWriteRequestExample : IExamplesProvider<DhcpScopeWriteRequest>
{
    /// <summary>
    ///     This method returns an ordinary home IPv4 scope: leases of a day by default and a week
    ///     at most, with one router and one DNS server.
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
