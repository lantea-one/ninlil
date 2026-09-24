using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Appearance</c> section of the application's
///     configuration and controls this host's own global default palette and light/dark mode for
///     the Angular admin SPA's own theming engine -- inherited by every signed-in user whose own
///     <c>UserEntity.PreferredPalette</c>/<c>PreferredMode</c> override is <see langword="null" />.
///     <para>
///         Unlike this project's other runtime settings option classes (<c>DnsRateLimitOptions</c>,
///         <c>DnsCookieOptions</c>, and so on, every one of which is bound via
///         <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> and therefore needs a
///         restart before a written change takes effect), this class is deliberately read via
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> wherever it is
///         consumed (<see cref="Controllers.AppearanceSettingsController" />). This is safe to do
///         here specifically because appearance has zero server-side runtime effect beyond that
///         controller reading the value back out -- no background service, cache, or long-lived
///         object anywhere else in this process ever captures a copy of it that a hot reload could
///         leave stale. A change written to the configuration file (via
///         <see cref="Ninlil.Dns.Server.Presets.PresetConfigurationFileApplier" />, the exact same atomic
///         merge mechanism every other runtime settings controller already uses) is therefore
///         visible on this controller's very next read, with no restart required -- the .NET
///         default JSON configuration provider already reloads on file change, and
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> is simply the one
///         binding surface in this codebase that actually surfaces that reload to its own callers.
///     </para>
/// </summary>
public sealed class AppearanceOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Appearance";

    /// <summary>
    ///     This property contains this host's own global default color palette, inherited by
    ///     every signed-in user whose own preference override is <see langword="null" />. Defaults
    ///     to <see cref="UiThemePaletteEnum.Teal" />.
    /// </summary>
    public UiThemePaletteEnum Palette { get; set; } = UiThemePaletteEnum.Teal;

    /// <summary>
    ///     This property contains this host's own global default light/dark mode, inherited by
    ///     every signed-in user whose own preference override is <see langword="null" />. Defaults
    ///     to <see cref="UiThemeModeEnum.System" />.
    /// </summary>
    public UiThemeModeEnum Mode { get; set; } = UiThemeModeEnum.System;
}
