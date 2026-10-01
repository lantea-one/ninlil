using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpReservations;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpReservationWriteRequest" />, the
///     request body of <see cref="Controllers.DhcpReservationsController.Create" /> and
///     <see cref="Controllers.DhcpReservationsController.Replace" />.
/// </summary>
public class DhcpReservationWriteRequestExample : IExamplesProvider<DhcpReservationWriteRequest>
{
    /// <summary>
    ///     This method returns a fixed address for a printer identified by its MAC address. The
    ///     address lies outside the 100 to 200 range of the "Home LAN" scope in
    ///     <see cref="DhcpScopes.DhcpScopeWriteRequestExample" />.
    /// </summary>
    public DhcpReservationWriteRequest GetExamples() =>
        new(
            "00:11:22:33:44:55",
            "192.168.1.50",
            HostName: "office-printer",
            IsDisabled: false,
            Comments: "Front office printer"
        );
}
