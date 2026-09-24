using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AppearanceSettingsDetail" />, referenced by
///     <see cref="Controllers.AppearanceSettingsController.Get" />.
/// </summary>
public class AppearanceSettingsDetailExample : IExamplesProvider<AppearanceSettingsDetail>
{
    /// <summary>
    ///     This method returns this server's own compiled-in defaults -- the teal palette,
    ///     following the browser's own operating-system light/dark preference.
    /// </summary>
    public AppearanceSettingsDetail GetExamples() =>
        new(UiThemePaletteEnum.Teal, UiThemeModeEnum.System);
}
