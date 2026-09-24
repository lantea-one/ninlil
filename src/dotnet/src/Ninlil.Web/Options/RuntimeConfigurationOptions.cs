// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:RuntimeSettings</c> section of the application's
///     configuration and controls which JSON file <see cref="Ninlil.Dns.Server.Presets.PresetApplier" /> and
///     this project's own runtime settings controllers (<c>PresetsController</c>,
///     <c>EventBrokerSettingsController</c>, <c>RateLimitingSettingsController</c>,
///     <c>DnsCookieSettingsController</c>) merge a write into. Defaults to this host's own
///     <c>appsettings.json</c>, but is itself overridable -- primarily so an integration test can
///     point every one of those controllers at an isolated, throwaway file instead of mutating the
///     real one a shared test-factory instance would otherwise leave dirty across runs.
///     <para>
///         Writing a setting here does not make it take effect immediately for every consumer --
///         only a component reading via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />
///         picks up a change without a restart; most of this project's option
///         classes are still read via the non-hot-reloading <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />,
///         so a restart is the honest, general answer for "when does this take effect."
///     </para>
/// </summary>
public sealed class RuntimeConfigurationOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:RuntimeSettings";

    /// <summary>
    ///     This property contains the JSON configuration file a runtime settings write is merged
    ///     into. <see langword="null" />/empty resolves to this host's own <c>appsettings.json</c>,
    ///     next to the running application, at the point <c>Program.cs</c> reads this section.
    /// </summary>
    public string ConfigurationFilePath { get; set; }
}
