using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupListAssignments;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupListAssignmentDetail" />, referenced by
///     <see cref="Controllers.ClientGroupListAssignmentsController.Get" /> and
///     <see cref="Controllers.ClientGroupListAssignmentsController.Create" />.
/// </summary>
public class ClientGroupListAssignmentDetailExample
    : IExamplesProvider<ClientGroupListAssignmentDetail>
{
    /// <summary>
    ///     This method returns the same assignment
    ///     <see cref="ClientGroupListAssignmentWriteRequestExample" /> submits, as this server
    ///     would report it back -- the "Trusted LAN" group
    ///     (<see cref="ClientGroups.ClientGroupDetailExample" />'s second example) assigned the
    ///     subscribed "Hagezi Pro Blocklist".
    /// </summary>
    public ClientGroupListAssignmentDetail GetExamples() =>
        new(
            Guid.Parse("f6a1b2c3-a3b4-4cd3-e5f6-a1b2c3d4e5f1"),
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
            Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
            DateTimeOffset.Parse("2026-07-10T13:25:00+00:00")
        );
}
