using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientWriteRequest" />, referenced by <see cref="Controllers.ClientsController.Replace" />.
///     Deliberately carries no <c>clientIdentifier</c>/<c>protocolVersion</c> fields at all -- see
///     <see cref="Controllers.ClientsController" />'s own doc comment for why neither can ever
///     change once a client is registered.
/// </summary>
public class ClientWriteRequestExample : IExamplesProvider<ClientWriteRequest>
{
    /// <summary>
    ///     This method returns a rename, re-assigning this client to a different client group.
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
