// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective branding
///     configuration -- <c>BrandingOptions</c>'s own site title/logo, alongside the already-
///     existing <c>AuthoritativeServerOptions</c> fields that drive the real CHAOS-class
///     <c>version.bind</c>/<c>hostname.bind</c> TXT query answers, exposed here on the same
///     settings surface rather than a separate one.
/// </summary>
/// <param name="SiteTitle">
///     This host's own custom browser-tab/page title, or <see langword="null" /> when unset (in
///     which case the Angular admin SPA keeps its own compiled-in default title).
/// </param>
/// <param name="LogoDataUri">
///     This host's own custom logo, as a complete data URI, or <see langword="null" /> when no
///     custom logo has been uploaded.
/// </param>
/// <param name="HostnameString">
///     The value this server answers a <c>hostname.bind</c>/<c>id.server</c> CHAOS query with,
///     when <paramref name="EnableChaosVersionQueries" /> is enabled.
/// </param>
/// <param name="VersionString">
///     The value this server answers a <c>version.bind</c>/<c>version.server</c> CHAOS query
///     with, when <paramref name="EnableChaosVersionQueries" /> is enabled.
/// </param>
/// <param name="EnableChaosVersionQueries">
///     Whether this server answers the traditional CHAOS-class fingerprinting queries at all.
/// </param>
public record BrandingSettingsDetail(
    string SiteTitle,
    string LogoDataUri,
    string HostnameString,
    string VersionString,
    bool EnableChaosVersionQueries
);

/// <summary>
///     This record is the REST representation of the small, non-sensitive slice of this host's
///     own branding an authenticated caller may read with no specific permission at all --
///     mirroring <c>AppearanceSettingsController.GetMine</c>'s own "self-service, no permission
///     needed" bar. Knowing the site's own configured title/logo carries no confidentiality
///     concern the way editing it (or reading the CHAOS-query fingerprinting fields, which stay
///     behind <c>branding:read</c>) would -- every signed-in session, regardless of role, needs
///     this to render the SPA's own page title/branding correctly.
/// </summary>
/// <param name="SiteTitle">This host's own custom browser-tab/page title, or <see langword="null" />.</param>
/// <param name="LogoDataUri">This host's own custom logo, as a complete data URI, or <see langword="null" />.</param>
public record BrandingEffectiveDetail(string SiteTitle, string LogoDataUri);
