using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>AppearanceOptions</c> -- the global default palette and light/dark mode every signed-in
///     user's own preference inherits when they have not set an override of their own.
/// </summary>
/// <param name="Palette">This host's own global default color palette.</param>
/// <param name="Mode">This host's own global default light/dark mode.</param>
public record AppearanceSettingsDetail(UiThemePaletteEnum Palette, UiThemeModeEnum Mode);

/// <summary>
///     This record is the REST representation of the current caller's own appearance preference:
///     their own override, if any, alongside the effective value the Angular admin SPA's own
///     theming engine should actually render, after falling back to this host's own global default
///     for whichever field (if either) is not overridden.
/// </summary>
/// <param name="Palette">
///     The caller's own palette override, or <see langword="null" /> if they have never set one
///     (in which case <paramref name="EffectivePalette" /> is this host's own global default).
/// </param>
/// <param name="Mode">
///     The caller's own mode override, or <see langword="null" /> if they have never set one (in
///     which case <paramref name="EffectiveMode" /> is this host's own global default).
/// </param>
/// <param name="EffectivePalette">
///     The palette the Angular admin SPA's own theming engine should actually render for this
///     caller right now -- <paramref name="Palette" /> if set, otherwise this host's own current
///     global default.
/// </param>
/// <param name="EffectiveMode">
///     The mode the Angular admin SPA's own theming engine should actually render for this caller
///     right now -- <paramref name="Mode" /> if set, otherwise this host's own current global
///     default.
/// </param>
public record UserAppearancePreferenceDetail(
    UiThemePaletteEnum? Palette,
    UiThemeModeEnum? Mode,
    UiThemePaletteEnum EffectivePalette,
    UiThemeModeEnum EffectiveMode
);
