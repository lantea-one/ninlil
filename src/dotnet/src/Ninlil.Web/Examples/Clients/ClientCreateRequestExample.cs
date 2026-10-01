using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientCreateRequest" />, the request
///     body of <see cref="Controllers.ClientsController.Create" />.
/// </summary>
public class ClientCreateRequestExample : IExamplesProvider<ClientCreateRequest>
{
    /// <summary>
    ///     This method returns a manually registered IPv4 client assigned to the "Trusted LAN"
    ///     group, with an icon and the favorite star.
    /// </summary>
    public ClientCreateRequest GetExamples() =>
        new(
            "AA:BB:CC:DD:EE:FF",
            InternetProtocolVersionEnum.IPv4,
            "Living Room TV",
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
            IsDisabled: false,
            Comments: "Confirmed via its MAC address label.",
            DeviceIconKey: "Television",
            IsFavorite: true
        );
}
