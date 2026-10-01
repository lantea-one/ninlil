using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroups;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="ClientGroupDetail" />, as
///     returned by <see cref="Controllers.ClientGroupsController.List" />.
/// </summary>
public class ClientGroupDetailPagedResultExample : IExamplesProvider<PagedResult<ClientGroupDetail>>
{
    /// <summary>
    ///     This method returns a page holding the default fallback group and the paused non-default
    ///     group from <see cref="ClientGroupDetailExample" />.
    /// </summary>
    public PagedResult<ClientGroupDetail> GetExamples() =>
        new(
            [
                new ClientGroupDetail(
                    Guid.Parse("b2c3d4e5-6f70-4839-a1b2-c3d4e5f6a1b1"),
                    "Everyone Else",
                    true,
                    false,
                    DateTimeOffset.Parse("2026-06-01T09:00:00+00:00"),
                    null,
                    null,
                    false
                ),
                new ClientGroupDetail(
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "Trusted LAN",
                    false,
                    false,
                    DateTimeOffset.Parse("2026-06-01T09:05:00+00:00"),
                    DateTimeOffset.Parse("2026-07-10T13:20:00+00:00"),
                    DateTimeOffset.Parse("2026-09-17T23:00:00+00:00"),
                    true
                ),
            ],
            0,
            50,
            2
        );
}
