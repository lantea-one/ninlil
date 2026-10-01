using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpScopes;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="DhcpScopeDetail" />, as
///     returned by <see cref="Controllers.DhcpScopesController.List" />.
/// </summary>
public class DhcpScopeDetailPagedResultExample : IExamplesProvider<PagedResult<DhcpScopeDetail>>
{
    /// <summary>
    ///     This method returns a one-item page holding the "Home LAN" scope without PXE boot
    ///     settings.
    /// </summary>
    public PagedResult<DhcpScopeDetail> GetExamples() =>
        new(
            [
                new DhcpScopeDetail(
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
                    null,
                    null,
                    DhcpScopeKindEnum.AddressAssignment,
                    null
                ),
            ],
            0,
            50,
            1
        );
}
