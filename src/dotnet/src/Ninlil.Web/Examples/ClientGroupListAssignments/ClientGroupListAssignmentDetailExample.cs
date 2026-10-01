using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupListAssignments;

/// <summary>
///     This class supplies the Swagger example for <see cref="ClientGroupListAssignmentDetail" />,
///     as returned by <see cref="Controllers.ClientGroupListAssignmentsController.Get" /> and
///     <see cref="Controllers.ClientGroupListAssignmentsController.Create" />.
/// </summary>
public class ClientGroupListAssignmentDetailExample
    : IExamplesProvider<ClientGroupListAssignmentDetail>
{
    /// <summary>
    ///     This method returns the assignment
    ///     <see cref="ClientGroupListAssignmentWriteRequestExample" /> submits, as the server
    ///     reports it: the "Trusted LAN" group from
    ///     <see cref="ClientGroups.ClientGroupDetailExample" /> assigned the subscribed "Hagezi Pro
    ///     Blocklist".
    /// </summary>
    public ClientGroupListAssignmentDetail GetExamples() =>
        new(
            Guid.Parse("f6a1b2c3-a3b4-4cd3-e5f6-a1b2c3d4e5f1"),
            Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
            Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
            DateTimeOffset.Parse("2026-07-10T13:25:00+00:00")
        );
}
