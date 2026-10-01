using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientListAssignments;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="ClientListAssignmentDetail" />, as returned by
///     <see cref="Controllers.ClientListAssignmentsController.List" />.
/// </summary>
public class ClientListAssignmentDetailPagedResultExample
    : IExamplesProvider<PagedResult<ClientListAssignmentDetail>>
{
    /// <summary>
    ///     This method returns two direct assignments for the "Living Room TV" client, the "Hagezi
    ///     Pro Blocklist" from <see cref="ClientListAssignmentDetailExample" /> and the local
    ///     "Internal Allowlist", showing that a client can hold several.
    /// </summary>
    public PagedResult<ClientListAssignmentDetail> GetExamples() =>
        new(
            [
                new ClientListAssignmentDetail(
                    Guid.Parse("a2b3c4d5-c4d5-4ed3-a4b2-c3d4e5f6a1c3"),
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
                    Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
                    DateTimeOffset.Parse("2026-09-16T14:05:00+00:00")
                ),
                new ClientListAssignmentDetail(
                    Guid.Parse("b3c4d5e6-d5e6-4fe4-b5c3-d4e5f6a1b2d4"),
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    DateTimeOffset.Parse("2026-09-16T14:06:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
