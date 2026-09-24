using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpReservations;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpReservationWriteRequest" />, referenced by
///     <see cref="Controllers.DhcpReservationsController.Create" /> and
///     <see cref="Controllers.DhcpReservationsController.Replace" />.
/// </summary>
public class DhcpReservationWriteRequestExample : IExamplesProvider<DhcpReservationWriteRequest>
{
    /// <summary>
    ///     This method returns a fixed address reservation for a printer, identified by its MAC
    ///     address.
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
