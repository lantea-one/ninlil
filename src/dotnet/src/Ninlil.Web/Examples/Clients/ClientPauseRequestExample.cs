using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the Swagger examples for <see cref="InstantPauseRequest" />, the request
///     body of both <see cref="Controllers.ClientsController.Pause" /> and
///     <see cref="Controllers.ClientGroupsController.Pause" />, which take the same shape.
/// </summary>
public class ClientPauseRequestExample : IMultipleExamplesProvider<InstantPauseRequest>
{
    /// <summary>
    ///     This method returns three durations: the 15-minute and 1-hour choices the admin
    ///     console's pause buttons send, and 600 minutes, about what its "until tomorrow morning"
    ///     button computes from an evening start.
    /// </summary>
    public IEnumerable<SwaggerExample<InstantPauseRequest>> GetExamples() =>
        [
            new()
            {
                Name = "15 minutes",
                Summary = "A short, quick-recheck pause.",
                Value = new InstantPauseRequest(15),
            },
            new()
            {
                Name = "1 hour",
                Summary = "A typical dinner-time or homework-time pause.",
                Value = new InstantPauseRequest(60),
            },
            new()
            {
                Name = "Until tomorrow morning",
                Summary = "A long overnight-style pause, expressed as a duration in minutes.",
                Value = new InstantPauseRequest(600),
            },
        ];
}
