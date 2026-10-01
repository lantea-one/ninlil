using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Appearance</c> configuration section: the server-wide
///     default palette and light/dark mode of the admin console, which a signed-in user gets when
///     their <c>UserEntity.PreferredPalette</c> or <c>PreferredMode</c> is <see langword="null" />.
///     <para>
///         <see cref="Controllers.AppearanceSettingsController" />, its only reader, reads it
///         through <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />. Nothing
///         else in the process keeps a copy, so a change merged into the configuration file by
///         <see cref="Ninlil.Dns.Server.Presets.PresetConfigurationFileApplier" /> shows on the
///         next read without a restart, once the JSON configuration provider reloads the file.
///     </para>
/// </summary>
public sealed class AppearanceOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Appearance";

    /// <summary>
    ///     This property contains the default color palette for users who have not chosen one.
    ///     Defaults to <see cref="UiThemePaletteEnum.Teal" />.
    /// </summary>
    public UiThemePaletteEnum Palette { get; set; } = UiThemePaletteEnum.Teal;

    /// <summary>
    ///     This property contains the default light/dark mode for users who have not chosen one.
    ///     Defaults to <see cref="UiThemeModeEnum.System" />, which follows the operating system.
    /// </summary>
    public UiThemeModeEnum Mode { get; set; } = UiThemeModeEnum.System;
}
