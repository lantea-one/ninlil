using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientListAssignments;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientListAssignmentDetail" />, as
///     returned by <see cref="Controllers.ClientListAssignmentsController.Get" /> and
///     <see cref="Controllers.ClientListAssignmentsController.Create" />.
/// </summary>
public class ClientListAssignmentDetailExample : IExamplesProvider<ClientListAssignmentDetail>
{
    /// <summary>
    ///     This method returns the assignment
    ///     <see cref="ClientListAssignmentWriteRequestExample" /> submits, as the server reports
    ///     it: the "Living Room TV" client from <see cref="Clients.ClientDetailExample" /> assigned
    ///     the "Hagezi Pro Blocklist" directly, in addition to whatever its client group
    ///     contributes.
    /// </summary>
    public ClientListAssignmentDetail GetExamples() =>
        new(
            Guid.Parse("a2b3c4d5-c4d5-4ed3-a4b2-c3d4e5f6a1c3"),
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
            Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
            DateTimeOffset.Parse("2026-09-16T14:05:00+00:00")
        );
}
