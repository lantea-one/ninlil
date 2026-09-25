// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This class binds to the <c>DnsServer:Cookies</c> section of the application's
///     configuration and controls <see cref="DnsCookieService" />. The DNS Cookie settings
///     endpoint edits it; changes apply after a restart.
/// </summary>
public sealed class DnsCookieOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Cookies";

    /// <summary>
    ///     This property controls whether this server generates and checks DNS Cookies (RFC 7873).
    ///     On by default: a client that does not support cookies never sends one and is
    ///     unaffected, and offering cookies opens no new attack surface.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property controls whether a query without a valid server cookie is rejected
    ///     (BADCOOKIE, or REFUSED when it has no usable client cookie) instead of answered. Off by
    ///     default. Turning it on resists spoofing better but breaks every client that does not
    ///     send cookies or retry with the one it is given.
    /// </summary>
    public bool Require { get; set; }

    /// <summary>
    ///     This property contains how often the server cookie secret is replaced. The previous
    ///     secret is still accepted until the next rotation, so a cookie issued just before a
    ///     rotation keeps validating.
    /// </summary>
    public TimeSpan SecretRotationInterval { get; set; } = TimeSpan.FromHours(24);
}
