using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroupListAssignments;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="ClientGroupListAssignmentDetail" />, referenced by
///     <see cref="Controllers.ClientGroupListAssignmentsController.List" />.
/// </summary>
public class ClientGroupListAssignmentDetailPagedResultExample
    : IExamplesProvider<PagedResult<ClientGroupListAssignmentDetail>>
{
    /// <summary>
    ///     This method returns one page of two assignments for the "Trusted LAN" group -- the
    ///     subscribed "Hagezi Pro Blocklist" <see cref="ClientGroupListAssignmentDetailExample" />
    ///     already shows, plus the local "Internal Allowlist", together demonstrating that a group
    ///     may hold more than one assignment at once.
    /// </summary>
    public PagedResult<ClientGroupListAssignmentDetail> GetExamples() =>
        new(
            [
                new ClientGroupListAssignmentDetail(
                    Guid.Parse("f6a1b2c3-a3b4-4cd3-e5f6-a1b2c3d4e5f1"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
                    DateTimeOffset.Parse("2026-07-10T13:25:00+00:00")
                ),
                new ClientGroupListAssignmentDetail(
                    Guid.Parse("a1b2c3d4-b4c5-4de4-f6a1-b2c3d4e5f6a2"),
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    DateTimeOffset.Parse("2026-07-10T13:26:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
