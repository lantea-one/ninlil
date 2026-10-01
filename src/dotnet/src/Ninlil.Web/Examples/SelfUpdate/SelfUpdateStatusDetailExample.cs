using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.SelfUpdate;

/// <summary>
///     This class supplies the Swagger example for <see cref="SelfUpdateStatusDetail" />, as
///     returned by <see cref="Controllers.SelfUpdateController.Get" /> and
///     <see cref="Controllers.SelfUpdateController.Check" />.
/// </summary>
public class SelfUpdateStatusDetailExample : IExamplesProvider<SelfUpdateStatusDetail>
{
    /// <summary>
    ///     This method returns the "update available" case, the one worth illustrating, with the
    ///     manual update instructions the server gives, since it does not update itself.
    /// </summary>
    public SelfUpdateStatusDetail GetExamples() =>
        new(
            true,
            "0.1.0.0",
            "0.4.0.0",
            true,
            "https://example.com/ninlil/releases/0.4.0",
            "Fixes a real DNSSEC validation edge case; adds native DNS64 support.",
            // A fixed date, not the clock, so the committed OpenAPI snapshot is the same on every regeneration.
            DateTimeOffset.Parse("2026-06-15T10:00:00+00:00"),
            null,
            "Version 0.4.0.0 is available (currently running 0.1.0.0). Download the new release "
                + "from https://example.com/ninlil/releases/0.4.0, replace this server's own "
                + "binary, and restart it -- this project has no automatic in-place updater, so "
                + "applying an update is always a manual step."
        );
}
