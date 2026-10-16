using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupNetworks;

/// <summary>
///     This class supplies the Swagger examples for <see cref="ClientGroupNetworkWriteRequest" />,
///     the request body of <see cref="Controllers.ClientGroupNetworksController.Create" /> and
///     <see cref="Controllers.ClientGroupNetworksController.Replace" />.
/// </summary>
public class ClientGroupNetworkWriteRequestExample
    : IMultipleExamplesProvider<ClientGroupNetworkWriteRequest>
{
    /// <summary>
    ///     This method returns an IPv4 and an IPv6 network, since a group usually has to match
    ///     clients of both families, and a DNS-over-HTTPS ClientID row, which leaves <c>cidr</c>
    ///     unset. A bare address (192.168.1.7) is accepted as one host.
    /// </summary>
    public IEnumerable<SwaggerExample<ClientGroupNetworkWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "IPv4 /24 network",
                Summary = "A private IPv4 LAN segment.",
                Value = new ClientGroupNetworkWriteRequest("192.168.1.0/24"),
            },
            new()
            {
                Name = "IPv6 /64 network",
                Summary =
                    "A single IPv6 subnet, the smallest prefix a real deployment typically routes.",
                Value = new ClientGroupNetworkWriteRequest("2001:db8:1::/64"),
            },
            new()
            {
                Name = "DNS-over-HTTPS ClientID",
                Summary =
                    "Matches a DoH client by its own URL-path ClientID instead of a network -- "
                    + "leave 'cidr' unset when submitting this shape.",
                Value = new ClientGroupNetworkWriteRequest(null, "homelab-laptop"),
            },
        ];
}
