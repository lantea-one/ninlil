using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the Swagger example for the <see cref="RuntimeSettingsWriteResult" />
///     that <see cref="Controllers.AppearanceSettingsController.Patch" /> returns. It replaces the
///     shared <see cref="Examples.Settings.RuntimeSettingsWriteResultExample" />, whose
///     <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" /> would be wrong here:
///     appearance settings are read through
///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, so the endpoint
///     returns <see cref="RuntimeSettingsWriteResult.HotReloadedNote" />.
/// </summary>
public class AppearanceSettingsPatchResultExample : IExamplesProvider<RuntimeSettingsWriteResult>
{
    /// <summary>
    ///     This method returns the note that the change applies at once.
    /// </summary>
    public RuntimeSettingsWriteResult GetExamples() =>
        new(RuntimeSettingsWriteResult.HotReloadedNote);
}
