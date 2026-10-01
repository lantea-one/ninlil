// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the RFC 7873 DNS Cookie settings
///     (<c>DnsCookieOptions</c>) that <c>GET /api/dns-cookie-settings</c> returns. A <c>PATCH</c>
///     to the same endpoint accepts <see cref="Enable" />, <see cref="Require" /> and
///     <see cref="SecretRotationInterval" />; the last two fields are read-only cluster sync
///     bookkeeping.
/// </summary>
/// <param name="Enable">Whether the server sends and checks DNS Cookies. Defaults to <see langword="true" />.</param>
/// <param name="Require">Whether a query without a valid server cookie is rejected instead of answered. Defaults to <see langword="false" />.</param>
/// <param name="SecretRotationInterval">How often the server cookie secret is replaced. Defaults to 24 hours.</param>
/// <param name="LastModifiedUtc">When the settings were last written, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">The cluster node the last write came from, or <see langword="null" /> if never written.</param>
public record DnsCookieSettingsDetail(
    bool Enable,
    bool Require,
    TimeSpan SecretRotationInterval,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);
