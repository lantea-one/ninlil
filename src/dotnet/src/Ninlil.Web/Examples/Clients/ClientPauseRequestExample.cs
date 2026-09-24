using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="InstantPauseRequest" />, shared by <see cref="Controllers.ClientsController.Pause" />
///     and <see cref="Controllers.ClientGroupsController.Pause" /> -- the request shape is
///     identical at both the client and client-group level, so one example serves both.
/// </summary>
public class ClientPauseRequestExample : IMultipleExamplesProvider<InstantPauseRequest>
{
    /// <summary>
    ///     This method returns three of the quick-duration choices the Angular UI's own one-click
    ///     pause buttons submit -- 15 minutes, 1 hour, and a longer "rest of the day" style span --
    ///     so a caller sees the range of durations this action is meant for.
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
