using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.AppearanceSettingsController.Patch" />'s own <see cref="RuntimeSettingsWriteResult" />
///     response -- a dedicated example rather than the shared <see cref="Examples.Settings.RuntimeSettingsWriteResultExample" />,
///     since that shared example's own <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />
///     would misdescribe this specific endpoint: <c>AppearanceOptions</c> is bound via
///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so this endpoint
///     actually returns <see cref="RuntimeSettingsWriteResult.HotReloadedNote" /> instead.
/// </summary>
public class AppearanceSettingsPatchResultExample : IExamplesProvider<RuntimeSettingsWriteResult>
{
    /// <summary>
    ///     This method returns this endpoint's own real, accurate hot-reload note.
    /// </summary>
    public RuntimeSettingsWriteResult GetExamples() =>
        new(RuntimeSettingsWriteResult.HotReloadedNote);
}
