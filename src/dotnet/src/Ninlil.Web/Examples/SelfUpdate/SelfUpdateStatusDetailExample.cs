using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SelfUpdate;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="SelfUpdateStatusDetail" />, referenced by
///     <see cref="Controllers.SelfUpdateController.Get" />/
///     <see cref="Controllers.SelfUpdateController.Check" />.
/// </summary>
public class SelfUpdateStatusDetailExample : IExamplesProvider<SelfUpdateStatusDetail>
{
    /// <summary>
    ///     This method returns a real, plausible "an update is available" status -- the shape an
    ///     operator most needs to see illustrated, since "nothing to do" is otherwise the most
    ///     common response.
    /// </summary>
    public SelfUpdateStatusDetail GetExamples() =>
        new(
            true,
            "0.1.0.0",
            "0.4.0.0",
            true,
            "https://example.com/ninlil/releases/0.4.0",
            "Fixes a real DNSSEC validation edge case; adds native DNS64 support.",
            DateTimeOffset.UtcNow,
            null,
            "Version 0.4.0.0 is available (currently running 0.1.0.0). Download the new release "
                + "from https://example.com/ninlil/releases/0.4.0, replace this server's own "
                + "binary, and restart it -- this project has no automatic in-place updater, so "
                + "applying an update is always a manual step."
        );
}
