using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientListAssignments;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientListAssignmentDetail" />, referenced by
///     <see cref="Controllers.ClientListAssignmentsController.Get" /> and
///     <see cref="Controllers.ClientListAssignmentsController.Create" />.
/// </summary>
public class ClientListAssignmentDetailExample : IExamplesProvider<ClientListAssignmentDetail>
{
    /// <summary>
    ///     This method returns the same assignment
    ///     <see cref="ClientListAssignmentWriteRequestExample" /> submits, as this server would
    ///     report it back -- the "Living Room TV" client
    ///     (<see cref="Clients.ClientDetailExample" />'s first example) assigned the subscribed
    ///     "Hagezi Pro Blocklist" directly, on top of whatever its own matched client group
    ///     already contributes.
    /// </summary>
    public ClientListAssignmentDetail GetExamples() =>
        new(
            Guid.Parse("a2b3c4d5-c4d5-4ed3-a4b2-c3d4e5f6a1c3"),
            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
            Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
            DateTimeOffset.Parse("2026-09-16T14:05:00+00:00")
        );
}
