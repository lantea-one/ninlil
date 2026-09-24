using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpScopeDetail" />, referenced by
///     <see cref="Controllers.DhcpScopesController.Get" /> and
///     <see cref="Controllers.DhcpScopesController.Create" />.
/// </summary>
public class DhcpScopeDetailExample : IExamplesProvider<DhcpScopeDetail>
{
    /// <summary>
    ///     This method returns the same home-LAN scope
    ///     <see cref="DhcpScopeWriteRequestExample" /> submits, as this server would report it
    ///     back, with its own identifier and timestamps.
    /// </summary>
    public DhcpScopeDetail GetExamples() =>
        new(
            Guid.Parse("a1b2c3d4-5e6f-4708-9192-a3b4c5d6e7f1"),
            "Home LAN",
            InternetProtocolVersionEnum.IPv4,
            "192.168.1.100",
            "192.168.1.200",
            24,
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(7),
            false,
            ["192.168.1.1"],
            ["192.168.1.1"],
            "Main household network",
            DateTimeOffset.Parse("2026-06-01T09:00:00+00:00"),
            null,
            null,
            "pxelinux.0",
            "192.168.1.10",
            DhcpScopeKindEnum.AddressAssignment,
            null
        );
}
