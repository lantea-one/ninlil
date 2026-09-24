using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupNetworks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="ClientGroupNetworkDetail" />, referenced by
///     <see cref="Controllers.ClientGroupNetworksController.List" />.
/// </summary>
public class ClientGroupNetworkDetailPagedResultExample
    : IExamplesProvider<PagedResult<ClientGroupNetworkDetail>>
{
    /// <summary>
    ///     This method returns one page of the same IPv4 and IPv6 networks
    ///     <see cref="ClientGroupNetworkDetailExample" /> already shows individually, so a caller
    ///     reading this example sees both address families together, in the exact envelope a real
    ///     list response carries.
    /// </summary>
    public PagedResult<ClientGroupNetworkDetail> GetExamples() =>
        new(
            [
                new ClientGroupNetworkDetail(
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d1"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "192.168.1.0/24",
                    null,
                    DateTimeOffset.Parse("2026-06-01T09:10:00+00:00")
                ),
                new ClientGroupNetworkDetail(
                    Guid.Parse("e5f6a1b2-92a3-4bc2-d4e5-f6a1b2c3d4e2"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "2001:db8:1::/64",
                    null,
                    DateTimeOffset.Parse("2026-06-01T09:11:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
