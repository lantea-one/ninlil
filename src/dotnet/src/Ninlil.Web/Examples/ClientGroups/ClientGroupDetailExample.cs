using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroups;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupDetail" />, referenced by
///     <see cref="Controllers.ClientGroupsController.Get" /> and
///     <see cref="Controllers.ClientGroupsController.Create" />.
/// </summary>
public class ClientGroupDetailExample : IMultipleExamplesProvider<ClientGroupDetail>
{
    /// <summary>
    ///     This method returns the same two groups <see cref="ClientGroupWriteRequestExample" />
    ///     submits, as this server would report them back, each with its own identifier and
    ///     timestamps.
    /// </summary>
    public IEnumerable<SwaggerExample<ClientGroupDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Default fallback group",
                Summary =
                    "The group every client not matched by another group's own networks falls into.",
                Value = new ClientGroupDetail(
                    Guid.Parse("b2c3d4e5-6f70-4839-a1b2-c3d4e5f6a1b1"),
                    "Everyone Else",
                    true,
                    false,
                    DateTimeOffset.Parse("2026-06-01T09:00:00+00:00"),
                    null,
                    null,
                    false
                ),
            },
            new()
            {
                Name = "Trusted LAN clients, currently paused",
                Summary =
                    "A non-default group matched by its own explicit networks only, currently under a one-click instant pause affecting every member.",
                Value = new ClientGroupDetail(
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    "Trusted LAN",
                    false,
                    false,
                    DateTimeOffset.Parse("2026-06-01T09:05:00+00:00"),
                    DateTimeOffset.Parse("2026-07-10T13:20:00+00:00"),
                    DateTimeOffset.Parse("2026-09-17T23:00:00+00:00"),
                    true
                ),
            },
        ];
}
