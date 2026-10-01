using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientWriteRequest" />, the request
///     body of <see cref="Controllers.ClientsController.Replace" />. It has no
///     <c>clientIdentifier</c> or <c>protocolVersion</c>, because an address's identity never
///     changes once registered (see <see cref="Controllers.ClientsController" />).
/// </summary>
public class ClientWriteRequestExample : IExamplesProvider<ClientWriteRequest>
{
    /// <summary>
    ///     This method returns a rename of the "Living Room TV" client that keeps its group,
    ///     comments, icon and favorite star.
    /// </summary>
    public ClientWriteRequest GetExamples() =>
        new(
            "Living Room TV (renamed)",
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
            IsDisabled: false,
            Comments: "Confirmed via its MAC address label.",
            DeviceIconKey: "Television",
            IsFavorite: true
        );
}
