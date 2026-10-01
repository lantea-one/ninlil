using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the Swagger example for <see cref="UserAppearancePreferenceDetail" />,
///     as returned by <see cref="Controllers.AppearanceSettingsController.GetMine" />.
/// </summary>
public class UserAppearancePreferenceDetailExample
    : IExamplesProvider<UserAppearancePreferenceDetail>
{
    /// <summary>
    ///     This method returns a caller who chose dark mode but no palette, so <c>Palette</c> is
    ///     <see langword="null" /> and <c>EffectivePalette</c> is the server-wide default.
    /// </summary>
    public UserAppearancePreferenceDetail GetExamples() =>
        new(null, UiThemeModeEnum.Dark, UiThemePaletteEnum.Teal, UiThemeModeEnum.Dark);
}
