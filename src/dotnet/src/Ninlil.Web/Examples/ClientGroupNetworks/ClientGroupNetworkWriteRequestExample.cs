using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupNetworks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupNetworkWriteRequest" />, referenced by
///     <see cref="Controllers.ClientGroupNetworksController.Create" /> and
///     <see cref="Controllers.ClientGroupNetworksController.Replace" />.
/// </summary>
public class ClientGroupNetworkWriteRequestExample
    : IMultipleExamplesProvider<ClientGroupNetworkWriteRequest>
{
    /// <summary>
    ///     This method returns both an IPv4 and an IPv6 CIDR network -- a real deployment's own
    ///     client group commonly needs to match clients arriving over either family, and
    ///     <see cref="Controllers.ClientGroupNetworksController" />'s own <c>System.Net.IPNetwork</c>
    ///     validation accepts both identically.
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
