using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupNetworks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupNetworkDetail" />, referenced by
///     <see cref="Controllers.ClientGroupNetworksController.Get" /> and
///     <see cref="Controllers.ClientGroupNetworksController.Create" />.
/// </summary>
public class ClientGroupNetworkDetailExample : IMultipleExamplesProvider<ClientGroupNetworkDetail>
{
    /// <summary>
    ///     This method returns the same IPv4 and IPv6 networks
    ///     <see cref="ClientGroupNetworkWriteRequestExample" /> submits, as this server would
    ///     report them back, both belonging to the "Trusted LAN" group
    ///     <see cref="ClientGroups.ClientGroupDetailExample" /> shows.
    /// </summary>
    public IEnumerable<SwaggerExample<ClientGroupNetworkDetail>> GetExamples() =>
        [
            new()
            {
                Name = "IPv4 /24 network",
                Summary = "A private IPv4 LAN segment.",
                Value = new ClientGroupNetworkDetail(
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d1"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "192.168.1.0/24",
                    null,
                    DateTimeOffset.Parse("2026-06-01T09:10:00+00:00")
                ),
            },
            new()
            {
                Name = "IPv6 /64 network",
                Summary =
                    "A single IPv6 subnet, the smallest prefix a real deployment typically routes.",
                Value = new ClientGroupNetworkDetail(
                    Guid.Parse("e5f6a1b2-92a3-4bc2-d4e5-f6a1b2c3d4e2"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "2001:db8:1::/64",
                    null,
                    DateTimeOffset.Parse("2026-06-01T09:11:00+00:00")
                ),
            },
            new()
            {
                Name = "DNS-over-HTTPS ClientID",
                Summary =
                    "A row matching a DoH client by its own URL-path ClientID rather than a network.",
                Value = new ClientGroupNetworkDetail(
                    Guid.Parse("f6a1b2c3-a3b4-4cd3-e5f6-a1b2c3d4e5f3"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    null,
                    "homelab-laptop",
                    DateTimeOffset.Parse("2026-09-03T09:12:00+00:00")
                ),
            },
        ];
}
