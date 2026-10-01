using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Presets;

/// <summary>
///     This class supplies the Swagger example for <see cref="PresetApplyResult" />, as returned by
///     <see cref="Controllers.PresetsController.Apply" />.
/// </summary>
public class PresetApplyResultExample : IExamplesProvider<PresetApplyResult>
{
    /// <summary>
    ///     This method returns the result of applying the <c>Baseline</c> preset, with the note
    ///     every preset application returns.
    /// </summary>
    public PresetApplyResult GetExamples() =>
        new(
            "Baseline",
            DateTimeOffset.Parse("2026-09-01T09:00:00+00:00"),
            "Applied. Settings bound via IOptionsMonitor take effect immediately; every other "
                + "setting this preset touched needs a restart to take effect."
        );
}
