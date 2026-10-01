// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the branding that <c>GET /api/branding-settings</c> returns: the site title
///     and logo from <c>BrandingOptions</c>, plus the <c>AuthoritativeServerOptions</c> fields that
///     answer the CHAOS-class <c>version.bind</c> and <c>hostname.bind</c> TXT queries. Both live
///     on one settings page because both decide how the server presents itself.
/// </summary>
/// <param name="SiteTitle">
///     The custom browser-tab and page title, or <see langword="null" /> when unset (the admin
///     console then keeps its built-in title).
/// </param>
/// <param name="LogoDataUri">
///     The custom logo as a complete data URI, or <see langword="null" /> when none has been
///     uploaded.
/// </param>
/// <param name="HostnameString">
///     The value the server answers a <c>hostname.bind</c> or <c>id.server</c> CHAOS query with,
///     when <paramref name="EnableChaosVersionQueries" /> is enabled.
/// </param>
/// <param name="VersionString">
///     The value the server answers a <c>version.bind</c> or <c>version.server</c> CHAOS query
///     with, when <paramref name="EnableChaosVersionQueries" /> is enabled.
/// </param>
/// <param name="EnableChaosVersionQueries">
///     Whether the server answers these CHAOS-class identification queries at all.
/// </param>
public record BrandingSettingsDetail(
    string SiteTitle,
    string LogoDataUri,
    string HostnameString,
    string VersionString,
    bool EnableChaosVersionQueries
);

/// <summary>
///     This record is the part of the branding any signed-in caller may read, with no specific
///     permission, from <c>GET /api/branding-settings/effective</c> (the same bar as
///     <c>AppearanceSettingsController.GetMine</c>). Every session needs the title and logo to
///     render the admin console, and they are not sensitive. The CHAOS-query fields stay behind
///     <c>branding:read</c>.
/// </summary>
/// <param name="SiteTitle">The custom browser-tab and page title, or <see langword="null" /> when unset.</param>
/// <param name="LogoDataUri">The custom logo as a complete data URI, or <see langword="null" /> when unset.</param>
public record BrandingEffectiveDetail(string SiteTitle, string LogoDataUri);
