using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientGroups;

/// <summary>
///     This class supplies the Swagger examples for <see cref="ClientGroupWriteRequest" />, the
///     request body of <see cref="Controllers.ClientGroupsController.Create" /> and
///     <see cref="Controllers.ClientGroupsController.Replace" />.
/// </summary>
public class ClientGroupWriteRequestExample : IMultipleExamplesProvider<ClientGroupWriteRequest>
{
    /// <summary>
    ///     This method returns the default fallback group
    ///     (<see cref="ClientGroupWriteRequest.IsDefault" /> <see langword="true" />), which takes
    ///     clients no other group's networks match, and an ordinary group. Only one group can be
    ///     the default (see <see cref="Controllers.ClientGroupsController" />).
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
