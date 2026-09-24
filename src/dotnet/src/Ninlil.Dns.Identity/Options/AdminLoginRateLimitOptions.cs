// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>DnsServer:AdminLoginRateLimit</c> section of the application's
///     configuration and controls this host's own brute-force protection for the two REST
///     endpoints an attacker could otherwise hammer to guess an administrator's credentials --
///     <c>Web.Controllers.AuthController.Login</c> and its own
///     <c>Web.Controllers.AuthController.TwoFactorLogin</c> second-factor challenge. Bound via
///     <c>Microsoft.Extensions.Options.IOptions{TOptions}</c>, read once at process startup by
///     <c>Extension.ServiceCollectionAdminLoginRateLimitingExtension</c>, since
///     <c>Microsoft.AspNetCore.RateLimiting</c>'s own partition/policy configuration is itself
///     only ever set up once, before this host's dependency injection container is even built --
///     the same restart-required posture <c>Web.Options.ReverseProxyOptions</c> already has for an
///     identical reason (a security-relevant middleware wired once at startup, not re-read live).
///     <para>
///         <b>This is a genuinely different threat model from <c>Server.RateLimiting</c>'s own
///         DNS-query rate limiter, and deliberately fails closed where that one deliberately fails
///         open.</b> <c>Server.RateLimiting.TokenBucketDnsRateLimiter</c> never denies a query past
///         its own tracked-client cap, specifically because DNS resolution itself must never
///         become unavailable due to the limiter's own behavior -- a false positive there breaks
///         every application depending on this server to resolve names at all. An admin sign-in
///         attempt carries no such requirement: rejecting a login past this limiter's own
///         threshold (via <c>Microsoft.AspNetCore.RateLimiting</c>'s own default behavior, a
///         <c>429 Too Many Requests</c> response, no queueing) is exactly the intended, correct
///         outcome of a credential-guessing attempt actually being blocked -- the temporary
///         inconvenience to a legitimate administrator who mistypes a password several times in a
///         row is a deliberate, accepted trade-off for genuinely stopping a brute-force attempt,
///         not a bug to work around.
///     </para>
///     <para>
///         Each client is rate-limited independently, partitioned by its own connecting IP address
///         (never by the submitted username) -- an attacker at one address can never lock out a
///         legitimate administrator signing in from a different address just by repeatedly
///         submitting that administrator's own username, the same "don't let a remote attacker
///         weaponize this feature against someone else" reasoning
///         <c>Server.RateLimiting</c>'s own per-client-IP partitioning already applies to the
///         entirely separate DNS-query path.
///     </para>
/// </summary>
public sealed class AdminLoginRateLimitOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:AdminLoginRateLimit";

    /// <summary>
    ///     This property controls whether this limiter actually rejects anything. When
    ///     <see langword="false" />, <c>Identity.Extension.ServiceCollectionAdminLoginRateLimitingExtension</c>
    ///     still registers its own named rate limiting policy (so
    ///     <c>Web.Controllers.AuthController.Login</c>/<c>TwoFactorLogin</c> never fail with a
    ///     configuration error for referencing an unregistered policy), just backed by a
    ///     no-op partition that never actually limits anything. Defaults to <see langword="true" />
    ///     -- a deliberate exception to this project's usual "capability-expanding toggle defaults
    ///     off" posture (architectural invariant #6), because this toggle does not expand
    ///     capability or attack surface at all: it is pure hardening of an already-exposed
    ///     endpoint, with no new attack vector of its own, the same "self-maintaining, defaults
    ///     on" reasoning this project already applies to DNSSEC validation and the persistent
    ///     query log.
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    ///     This property contains how many sign-in attempts (a <c>Login</c> or
    ///     <c>TwoFactorLogin</c> call, counted together per client address) are allowed within a
    ///     single <see cref="Window" /> before this limiter starts rejecting further attempts with
    ///     <c>429 Too Many Requests</c>. Defaults to 5.
    /// </summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>
    ///     This property contains the fixed window <see cref="PermitLimit" /> is counted over --
    ///     once a client's own attempt count within the current window reaches
    ///     <see cref="PermitLimit" />, every further attempt is rejected until the window rolls
    ///     over, which is also this limiter's own effective lockout duration. Defaults to five
    ///     minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}
