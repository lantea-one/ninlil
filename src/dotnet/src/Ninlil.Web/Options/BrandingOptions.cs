// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Branding</c> configuration section: the admin
///     console's page title and an operator-supplied logo. Branding applies to the whole install,
///     so there is no per-user override.
///     <para>
///         As with <see cref="AppearanceOptions" />, its only reader,
///         <see cref="Controllers.BrandingSettingsController" />, uses
///         <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> and nothing else
///         keeps a copy, so a change written to the configuration file shows on the next read
///         without a restart.
///     </para>
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Branding";

    /// <summary>
    ///     This constant contains the largest logo, in bytes before base64 encoding, that
    ///     <see cref="Controllers.BrandingSettingsController.Patch" /> accepts for
    ///     <see cref="LogoDataUri" />: 512 KiB, enough for a small logo while keeping the
    ///     configuration file, which stores the logo inline, from growing too large.
    /// </summary>
    public const int MaxLogoSizeBytes = 512 * 1024;

    /// <summary>
    ///     This property contains the custom browser-tab and page title, which the admin console
    ///     sets through <c>document.title</c> once it has loaded the value (see
    ///     <c>BrandingService</c> in the Angular app). <see langword="null" /> or empty keeps the
    ///     built-in title <c>Ninlil Admin</c> from <c>index.html</c>.
    /// </summary>
    public string SiteTitle { get; set; }

    /// <summary>
    ///     This property contains an operator-supplied logo as a complete data URI (for example
    ///     <c>data:image/png;base64,...</c>), checked with
    ///     <see cref="Ninlil.Common.Serializer.Model.DataUriModel" />. Keeping it as a string in the
    ///     configuration file avoids a separate file or blob store, works the same with every
    ///     storage engine, and needs no entry in <c>BackupEntityCatalog</c>. <see langword="null" />
    ///     means no custom logo. The size limit is <see cref="MaxLogoSizeBytes" />.
    /// </summary>
    public string LogoDataUri { get; set; }
}
