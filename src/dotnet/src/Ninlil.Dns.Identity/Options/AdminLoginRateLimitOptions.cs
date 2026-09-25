// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>DnsServer:AdminLoginRateLimit</c> section of the application's
///     configuration and controls brute-force protection for the two endpoints an attacker could
///     use to guess credentials: <c>Web.Controllers.AuthController.Login</c> and
///     <c>Web.Controllers.AuthController.TwoFactorLogin</c>.
///     <c>Extension.ServiceCollectionAdminLoginRateLimitingExtension</c> reads it once at startup,
///     because the <c>Microsoft.AspNetCore.RateLimiting</c> policy is set up before the dependency
///     injection container is built, so changes need a restart (as with
///     <c>Web.Options.ReverseProxyOptions</c>).
///     <para>
///         <b>It fails closed, unlike the DNS query limiter.</b>
///         <c>Server.RateLimiting.TokenBucketDnsRateLimiter</c> fails open past its tracked-client
///         cap because name resolution must not stop because of the limiter. A sign-in has no such
///         need: a <c>429 Too Many Requests</c> past the limit (the default rate-limiting
///         behavior, with no queueing) is the intended result, and the delay for an administrator
///         who mistypes a password a few times is an accepted cost.
///     </para>
///     <para>
///         Clients are limited per connecting IP address, never per submitted username, so an
///         attacker cannot lock out an administrator signing in from another address by
///         submitting that administrator's username.
///     </para>
/// </summary>
public sealed class AdminLoginRateLimitOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:AdminLoginRateLimit";

    /// <summary>
    ///     This property controls whether the limiter rejects anything. When
    ///     <see langword="false" />, <c>ServiceCollectionAdminLoginRateLimitingExtension</c> still
    ///     registers the named policy (so the sign-in endpoints do not fail for referencing a missing
    ///     policy), backed by a partition that never limits. It defaults to <see langword="true" />:
    ///     capability-expanding settings default to off, but this one only hardens an endpoint that
    ///     is already exposed, like DNSSEC validation and the query log, which also default on.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property contains how many sign-in attempts (<c>Login</c> and <c>TwoFactorLogin</c>
    ///     calls, counted together per client address) are allowed in one <see cref="Window" />
    ///     before further attempts get <c>429 Too Many Requests</c>. Defaults to 5.
    /// </summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>
    ///     This property contains the fixed window <see cref="PermitLimit" /> is counted over. Once a
    ///     client reaches the limit, further attempts are rejected until the window ends, so it is
    ///     also the lockout length. Defaults to five minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}
