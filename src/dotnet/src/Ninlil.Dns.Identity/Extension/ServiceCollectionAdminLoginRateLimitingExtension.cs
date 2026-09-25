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
///     This class provides our <see cref="IServiceCollection" /> extension for the admin sign-in
///     brute-force limiter, built on the ASP.NET Core <see cref="Microsoft.AspNetCore.RateLimiting" />
///     middleware. It applies only to the sign-in endpoints (see
///     <see cref="AuthenticationEndpointPolicyName" />) and is separate from the DNS query limiter
///     in <c>Server.RateLimiting</c>; <see cref="AdminLoginRateLimitOptions" /> explains why it
///     fails closed.
/// </summary>
public static class ServiceCollectionAdminLoginRateLimitingExtension
{
    /// <summary>
    ///     This constant contains the policy name that <c>Web.Controllers.AuthController.Login</c> and
    ///     <c>Web.Controllers.AuthController.TwoFactorLogin</c> apply with <c>[EnableRateLimiting]</c>.
    ///     No other endpoint uses it.
    /// </summary>
    public const string AuthenticationEndpointPolicyName = "admin-login";

    /// <summary>
    ///     This method registers <see cref="AdminLoginRateLimitOptions" /> and the
    ///     <see cref="AuthenticationEndpointPolicyName" /> policy: a fixed-window limiter partitioned
    ///     by client IP address (see <see cref="AdminLoginRateLimitOptions" /> for why not by user
    ///     name), with <see cref="FixedWindowRateLimiterOptions.QueueLimit" /> zero, so a request over
    ///     the limit gets <c>429 Too Many Requests</c> at once rather than waiting in a queue.
    ///     <para>
    ///         <b>The policy is registered even when <see cref="AdminLoginRateLimitOptions.Enable" />
    ///         is <see langword="false" />.</b> The middleware throws at request time if
    ///         <c>[EnableRateLimiting(name)]</c> names an unregistered policy, so a disabled limiter
    ///         registers the same name backed by <see cref="RateLimitPartition.GetNoLimiter{TKey}" />,
    ///         a partition that never limits.
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
            // Set explicitly so a change in the library default cannot alter the response.
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiterOptions.AddPolicy(
                AuthenticationEndpointPolicyName,
                httpContext =>
                {
                    // Partitioned by remote IP address, never by user name, so an attacker cannot lock
                    // a known administrator out. Connections without a remote address share one
                    // "unknown" partition.
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

        return instance;
    }
}
