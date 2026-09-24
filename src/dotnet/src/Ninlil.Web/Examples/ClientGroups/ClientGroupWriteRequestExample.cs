using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroups;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ClientGroupWriteRequest" />, referenced by
///     <see cref="Controllers.ClientGroupsController.Create" /> and
///     <see cref="Controllers.ClientGroupsController.Replace" />.
/// </summary>
public class ClientGroupWriteRequestExample : IMultipleExamplesProvider<ClientGroupWriteRequest>
{
    /// <summary>
    ///     This method returns two genuinely different group shapes -- the server-wide fallback
    ///     group (<see cref="ClientGroupWriteRequest.IsDefault" /> <see langword="true" />) every
    ///     client not matched by another group's own networks falls back to, and an ordinary,
    ///     non-default group -- so a caller reading this example sees both shapes
    ///     <see cref="Controllers.ClientGroupsController" />'s own "at most one default" rule
    ///     applies to.
    /// </summary>
    public IEnumerable<SwaggerExample<ClientGroupWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Default fallback group",
                Summary =
                    "The group every client not matched by another group's own networks falls into.",
                Value = new ClientGroupWriteRequest(
                    "Everyone Else",
                    IsDefault: true,
                    IsDisabled: false
                ),
            },
            new()
            {
                Name = "Trusted LAN clients",
                Summary = "A non-default group matched by its own explicit networks only.",
                Value = new ClientGroupWriteRequest(
                    "Trusted LAN",
                    IsDefault: false,
                    IsDisabled: false
                ),
            },
        ];
}
