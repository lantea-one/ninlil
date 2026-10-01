using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Settings;

/// <summary>
///     This class supplies the shared Swagger example for
///     <see cref="RuntimeSettingsWriteResult" />, used by the <c>PATCH</c> actions of most settings
///     controllers, such as <see cref="Controllers.QueryLoggingSettingsController.Patch" /> and
///     <see cref="Controllers.ReverseProxySettingsController.Patch" />. Many of those actions
///     return a different note, such as <see cref="RuntimeSettingsWriteResult.HotReloadedNote" />;
///     <see cref="Appearance.AppearanceSettingsPatchResultExample" /> is the one
///     controller-specific replacement.
/// </summary>
public class RuntimeSettingsWriteResultExample : IExamplesProvider<RuntimeSettingsWriteResult>
{
    /// <summary>
    ///     This method returns <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" />.
    /// </summary>
    public RuntimeSettingsWriteResult GetExamples() =>
        new(RuntimeSettingsWriteResult.RestartRequiredNote);
}
