using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Appearance;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="UserAppearancePreferenceDetail" />, referenced by
///     <see cref="Controllers.AppearanceSettingsController.GetMine" />.
/// </summary>
public class UserAppearancePreferenceDetailExample
    : IExamplesProvider<UserAppearancePreferenceDetail>
{
    /// <summary>
    ///     This method returns a caller who has overridden their own mode to dark, but never set
    ///     a palette override of their own -- so <c>Palette</c> is <see langword="null" /> while
    ///     <c>EffectivePalette</c> still reports this host's own current global default.
    /// </summary>
    public UserAppearancePreferenceDetail GetExamples() =>
        new(null, UiThemeModeEnum.Dark, UiThemePaletteEnum.Teal, UiThemeModeEnum.Dark);
}
