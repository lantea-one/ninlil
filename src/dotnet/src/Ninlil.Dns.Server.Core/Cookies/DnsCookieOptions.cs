// Define our namespace.
namespace Ninlil.Dns.Server.Core.Cookies;

/// <summary>
///     This class binds to the <c>DnsServer:Cookies</c> section of the application's
///     configuration and controls <see cref="DnsCookieService" />'s own behavior.
/// </summary>
public sealed class DnsCookieOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Cookies";

    /// <summary>
    ///     This property controls whether this server generates and validates DNS Cookies (RFC
    ///     7873) at all. Defaults <see langword="true" /> -- unlike a transport's own
    ///     capability-expanding <c>Enable</c> toggle, offering a cookie costs a cookie-unaware
    ///     client nothing (it simply never sends one back) and opens no new attack surface, the
    ///     same "self-maintaining, no corresponding abuse vector" reasoning already applied to
    ///     DNSSEC validation and RFC 5011.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property controls whether a query lacking a valid, currently-recognized server
    ///     cookie is refused outright rather than merely answered without one. Defaults
    ///     <see langword="false" /> ("supported, not required") -- flipping this on gives a
    ///     stricter anti-spoofing posture at the real cost of breaking any legacy client or
    ///     resolver that never learns to retry with the cookie this server hands back.
    /// </summary>
    public bool Require { get; set; }

    /// <summary>
    ///     This property contains how often this service replaces its own server-cookie signing
    ///     secret. The immediately preceding secret is kept around and still accepted for
    ///     validation until the next rotation, so a cookie issued just before a rotation does not
    ///     suddenly stop validating.
    /// </summary>
    public TimeSpan SecretRotationInterval { get; set; } = TimeSpan.FromHours(24);
}
