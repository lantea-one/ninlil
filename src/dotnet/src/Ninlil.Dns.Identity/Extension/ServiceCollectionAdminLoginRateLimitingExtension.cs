using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's admin-login brute-force rate limiter, via ASP.NET Core's own built-in
///     <see cref="Microsoft.AspNetCore.RateLimiting" /> middleware -- scoped specifically to the
///     sign-in endpoints (see <see cref="AuthenticationEndpointPolicyName" />), never the whole
///     API surface, and genuinely distinct from <c>Server.RateLimiting</c>'s own DNS-query
///     limiter (see <see cref="AdminLoginRateLimitOptions" />'s own doc comment for the full
///     "fails closed, not open" reasoning).
/// </summary>
public static class ServiceCollectionAdminLoginRateLimitingExtension
{
    /// <summary>
    ///     This constant contains the rate limiting policy name
    ///     <c>Web.Controllers.AuthController.Login</c> and
    ///     <c>Web.Controllers.AuthController.TwoFactorLogin</c> both apply via their own
    ///     <c>[EnableRateLimiting]</c> attribute -- no other endpoint in this project ever
    ///     references this policy, so this limiter's own cost is paid only by the two endpoints it
    ///     actually protects.
    /// </summary>
    public const string AuthenticationEndpointPolicyName = "admin-login";

    /// <summary>
    ///     This method registers <see cref="AdminLoginRateLimitOptions" /> and the real
    ///     <see cref="AuthenticationEndpointPolicyName" /> rate limiting policy -- a fixed-window
    ///     limiter partitioned per connecting client IP address (see
    ///     <see cref="AdminLoginRateLimitOptions" />'s own doc comment for why IP, never the
    ///     submitted username), with <see cref="FixedWindowRateLimiterOptions.QueueLimit" /> fixed
    ///     at zero -- a request past the limit is rejected immediately with
    ///     <c>429 Too Many Requests</c>, never queued and delayed, since queueing would soften
    ///     this limiter's own deliberate fail-closed posture into something closer to the DNS-side
    ///     limiter's fail-open one.
    ///     <para>
    ///         <b>The named policy is always registered, even when <see cref="AdminLoginRateLimitOptions.Enable" />
    ///         is <see langword="false" />.</b> <c>Microsoft.AspNetCore.RateLimiting</c>'s own
    ///         middleware throws at request time if an endpoint's
    ///         <c>[EnableRateLimiting(name)]</c> names a policy that was never registered at all --
    ///         found only by tracing through what actually happens to
    ///         <c>Web.Controllers.AuthController.Login</c>/<c>TwoFactorLogin</c> when this feature
    ///         is turned off, not by assumption. So a disabled configuration still registers this
    ///         same policy name, just backed by <see cref="RateLimitPartition.GetNoLimiter{TKey}" />
    ///         -- a real partition that never actually limits anything -- rather than skipping
    ///         registration outright; the two protected endpoints keep working identically to
    ///         every other endpoint in this project either way.
    ///     </para>
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read admin login rate limit settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsAdminLoginRateLimiting(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<AdminLoginRateLimitOptions>(
            configuration.GetSection(AdminLoginRateLimitOptions.SectionName)
        );

        AdminLoginRateLimitOptions options =
            configuration
                .GetSection(AdminLoginRateLimitOptions.SectionName)
                .Get<AdminLoginRateLimitOptions>()
            ?? new AdminLoginRateLimitOptions();

        instance.AddRateLimiter(rateLimiterOptions =>
        {
            // The default status code for a rejected request is already 429, matching this
            // limiter's own fail-closed intent -- set explicitly regardless, so a future change
            // to the library's own default can never silently soften this endpoint's own
            // response.
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiterOptions.AddPolicy(
                AuthenticationEndpointPolicyName,
                httpContext =>
                {
                    // Partitioned by the connecting client's own remote IP address -- never by
                    // the submitted username -- so a remote attacker can never weaponize this
                    // limiter to lock a specific, known administrator account out from a
                    // different address. A request whose connection somehow carries no remote
                    // address at all shares one fallback partition, exactly as
                    // Web.Controllers.AuthController's own refresh-token audit trail already
                    // falls back to "unknown" for the same missing-address case.
                    string partitionKey =
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return options.Enable
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey,
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = options.PermitLimit,
                                Window = options.Window,
                                QueueLimit = 0,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            }
                        )
                        : RateLimitPartition.GetNoLimiter(partitionKey);
                }
            );
        });

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
