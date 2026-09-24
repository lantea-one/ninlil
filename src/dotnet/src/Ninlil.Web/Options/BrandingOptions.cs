// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Branding</c> section of the application's
///     configuration and controls this host's own global custom branding -- the Angular admin
///     SPA's own page title and an operator-supplied logo -- inherited by every signed-in user,
///     with no per-user override, since branding is inherently install-wide.
///     <para>
///         Like <see cref="AppearanceOptions" />, this class is deliberately read via
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> wherever it is
///         consumed (<see cref="Controllers.BrandingSettingsController" />), rather than the
///         older, restart-requiring <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />.
///         This is safe here for the same reason it is safe for <see cref="AppearanceOptions" />:
///         branding has zero server-side runtime effect beyond that controller reading the value
///         back out -- no background service, cache, or long-lived object anywhere else in this
///         process ever captures a copy of it that a hot reload could leave stale. A change
///         written to the configuration file (via
///         <see cref="Ninlil.Dns.Server.Presets.PresetConfigurationFileApplier" />, the exact same atomic
///         merge mechanism every other runtime settings controller already uses) is therefore
///         visible on this controller's very next read, with no restart required.
///     </para>
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Branding";

    /// <summary>
    ///     This constant contains the maximum accepted logo size, in raw (pre-base64) bytes, that
    ///     <see cref="Controllers.BrandingSettingsController.Patch" /> accepts for
    ///     <see cref="LogoDataUri" /> -- generous enough for a real small logo image while keeping
    ///     the configuration file (which stores this value inline, base64-encoded, per this
    ///     project's confirmed "logo as a data URI" decision) from growing unreasonably large.
    /// </summary>
    public const int MaxLogoSizeBytes = 512 * 1024;

    /// <summary>
    ///     This property contains the Angular admin SPA's own custom browser-tab/page title,
    ///     applied live (via <c>document.title</c>) once the SPA has actually loaded this value --
    ///     see <c>BrandingService</c> on the Angular side. <see langword="null" />/empty means "use
    ///     this application's own compiled-in default title" (<c>Ninlil Admin</c>, set
    ///     statically in <c>index.html</c>).
    /// </summary>
    public string SiteTitle { get; set; }

    /// <summary>
    ///     This property contains an operator-supplied logo, stored as a complete data URI (for
    ///     example <c>data:image/png;base64,...</c>) -- confirmed via <c>AskUserQuestion</c>,
    ///     2026-09-01, as this feature's storage mechanism, reusing
    ///     <see cref="Ninlil.Common.Serializer.Model.DataUriModel" />'s own parsing/validation rather than
    ///     adding a new file/blob storage mechanism. This works identically across every storage
    ///     engine this project supports (it is just a string, config-file-stored, not a database
    ///     column or filesystem path) and travels with a full-system backup/restore automatically,
    ///     with no dedicated handling needed in <c>BackupEntityCatalog</c>. <see langword="null" />
    ///     means no custom logo has been uploaded. See <see cref="MaxLogoSizeBytes" /> for this
    ///     project's own accepted size ceiling.
    /// </summary>
    public string LogoDataUri { get; set; }
}
