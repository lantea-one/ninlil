using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientCreateRequest" />, referenced by <see cref="Controllers.ClientsController.Create" />.
/// </summary>
public class ClientCreateRequestExample : IExamplesProvider<ClientCreateRequest>
{
    /// <summary>
    ///     This method returns a manually registered IPv4 client, explicitly assigned to a
    ///     client group.
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
