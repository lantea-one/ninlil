using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the server-wide default appearance
///     (<c>AppearanceOptions</c>) that <c>GET /api/appearance-settings</c> returns: the palette and
///     light/dark mode a signed-in user gets when they have not chosen their own.
/// </summary>
/// <param name="Palette">The default color palette.</param>
/// <param name="Mode">The default light/dark mode.</param>
public record AppearanceSettingsDetail(UiThemePaletteEnum Palette, UiThemeModeEnum Mode);

/// <summary>
///     This record is the caller's appearance preference, returned by
///     <c>GET /api/appearance-settings/me</c>: their override for each field, if any, and the
///     value the admin console should render after falling back to the server-wide default.
/// </summary>
/// <param name="Palette">
///     The caller's palette override, or <see langword="null" /> if they have not set one (then
///     <paramref name="EffectivePalette" /> is the server-wide default).
/// </param>
/// <param name="Mode">
///     The caller's mode override, or <see langword="null" /> if they have not set one (then
///     <paramref name="EffectiveMode" /> is the server-wide default).
/// </param>
/// <param name="EffectivePalette">
///     The palette the admin console should render for the caller: <paramref name="Palette" /> if
///     set, otherwise the server-wide default.
/// </param>
/// <param name="EffectiveMode">
///     The mode the admin console should render for the caller: <paramref name="Mode" /> if set,
///     otherwise the server-wide default.
/// </param>
public record UserAppearancePreferenceDetail(
    UiThemePaletteEnum? Palette,
    UiThemeModeEnum? Mode,
    UiThemePaletteEnum EffectivePalette,
    UiThemeModeEnum EffectiveMode
);
