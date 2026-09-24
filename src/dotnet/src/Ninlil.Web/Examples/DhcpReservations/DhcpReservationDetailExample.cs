using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpReservations;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DhcpReservationDetail" />, referenced by
///     <see cref="Controllers.DhcpReservationsController.Get" /> and
///     <see cref="Controllers.DhcpReservationsController.Create" />.
/// </summary>
public class DhcpReservationDetailExample : IExamplesProvider<DhcpReservationDetail>
{
    /// <summary>
    ///     This method returns the same printer reservation
    ///     <see cref="DhcpReservationWriteRequestExample" /> submits, as this server would report
    ///     it back.
    /// </summary>
    public DhcpReservationDetail GetExamples() =>
        new(
            Guid.Parse("b2c3d4e5-6f70-4839-a1b2-c3d4e5f6a1b2"),
            Guid.Parse("a1b2c3d4-5e6f-4708-9192-a3b4c5d6e7f1"),
            "00:11:22:33:44:55",
            "192.168.1.50",
            "office-printer",
            false,
            "Front office printer",
            DateTimeOffset.Parse("2026-06-01T09:10:00+00:00"),
            null
        );
}
