using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the Swagger example for <see cref="AppearanceSettingsDetail" />, as
///     returned by <see cref="Controllers.AppearanceSettingsController.Get" />.
/// </summary>
public class AppearanceSettingsDetailExample : IExamplesProvider<AppearanceSettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults: the teal palette, with light or dark mode following
    ///     the operating system.
    /// </summary>
    public AppearanceSettingsDetail GetExamples() =>
        new(UiThemePaletteEnum.Teal, UiThemeModeEnum.System);
}
