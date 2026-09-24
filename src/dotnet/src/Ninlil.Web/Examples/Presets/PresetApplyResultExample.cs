using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Presets;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="PresetApplyResult" />, referenced by
///     <see cref="Controllers.PresetsController.Apply" />.
/// </summary>
public class PresetApplyResultExample : IExamplesProvider<PresetApplyResult>
{
    /// <summary>
    ///     This method returns the result of applying the <c>Baseline</c> preset, carrying the
    ///     same restart-caveat note every real application of this preset actually returns.
    /// </summary>
    public PresetApplyResult GetExamples() =>
        new(
            "Baseline",
            DateTimeOffset.Parse("2026-09-01T09:00:00+00:00"),
            "Applied. Settings bound via IOptionsMonitor take effect immediately; every other "
                + "setting this preset touched needs a restart to take effect."
        );
}
