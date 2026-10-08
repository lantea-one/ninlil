using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for the admin sign-in
///     brute-force limiter, built on the ASP.NET Core <see cref="Microsoft.AspNetCore.RateLimiting" />
///     middleware. It applies only to the sign-in endpoints (see
///     <see cref="AuthenticationEndpointPolicyName" />) and the Forgot Password request (see
///     <see cref="PasswordResetEndpointPolicyName" />), and is separate from the DNS query limiter
///     in <c>Server.RateLimiting</c>; <see cref="AdminLoginRateLimitOptions" /> explains why it
///     fails closed. The limiter counts per client network: the plain IPv4 address, or the /64
///     of an IPv6 address (see <see cref="AddNinlilDnsAdminLoginRateLimiting" />).
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
    ///     This constant contains the policy name that
    ///     <c>Web.Controllers.AuthController.ForgotPassword</c> applies, so an anonymous caller
    ///     cannot make the server send unlimited reset e-mails. It uses the same
    ///     <see cref="AdminLoginRateLimitOptions" /> limits as <see cref="AuthenticationEndpointPolicyName" />
    ///     but counts separately, so asking for a reset link does not use up sign-in attempts.
    /// </summary>
    public const string PasswordResetEndpointPolicyName = "password-reset";

    /// <summary>
    ///     This method registers <see cref="AdminLoginRateLimitOptions" /> and the
    ///     <see cref="AuthenticationEndpointPolicyName" /> and
    ///     <see cref="PasswordResetEndpointPolicyName" /> policies: each a fixed-window limiter partitioned
    ///     by client IP address, or by the /64 network for an IPv6 client (see
    ///     <see cref="AdminLoginRateLimitOptions" /> for why not by user name), with <see cref="FixedWindowRateLimiterOptions.QueueLimit" /> zero, so a request over
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

            // Each policy keeps its own partitions, so the two counters are independent.
            rateLimiterOptions.AddPolicy(AuthenticationEndpointPolicyName, GetPartition);
            rateLimiterOptions.AddPolicy(PasswordResetEndpointPolicyName, GetPartition);
        });

        return instance;

        // Partitioned by client network, never by user name, so an attacker cannot lock a known
        // administrator out. An IPv4 client (including one on a dual-stack socket) is keyed by
        // its plain IPv4 address. An IPv6 client is keyed by its /64, the smallest network a
        // provider normally assigns to one subscriber, because an attacker holding a /64 can
        // otherwise rotate through 2^64 addresses and never meet the limit. Connections without
        // a remote address share one "unknown" partition.
        static string GetPartitionKey(IPAddress remoteAddress)
        {
            IPAddress address = ClientAddress.Normalize(remoteAddress);

            if (address is null)
                return "unknown";

            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return address.ToString();

            byte[] network = address.GetAddressBytes();
            Array.Clear(network, 8, 8);
            return $"{new IPAddress(network)}/64";
        }

        RateLimitPartition<string> GetPartition(HttpContext httpContext)
        {
            string partitionKey = GetPartitionKey(httpContext.Connection.RemoteIpAddress);

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
    }
}
