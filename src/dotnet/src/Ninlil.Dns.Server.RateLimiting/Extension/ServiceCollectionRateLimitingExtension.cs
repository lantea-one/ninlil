using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     the DNS query rate limiter (per client IP and per subnet).
/// </summary>
public static class ServiceCollectionRateLimitingExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnsRateLimiter" /> (backed by
    ///     <see cref="TokenBucketDnsRateLimiter" />) and its cleanup service. Without this call,
    ///     the optional <c>rateLimiter</c> parameter of
    ///     <see cref="Server.Core.Pipeline.DnsQueryProcessingPipeline" /> stays
    ///     <see langword="null" />, which behaves the same as registering it with
    ///     <see cref="DnsRateLimitOptions.Enable" /> left <see langword="false" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read rate-limiting settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerRateLimiting(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<DnsRateLimitOptions>(
            configuration.GetSection(DnsRateLimitOptions.SectionName)
        );

        instance.AddSingleton<TokenBucketDnsRateLimiter>();
        instance.AddSingleton<IDnsRateLimiter>(provider =>
            provider.GetRequiredService<TokenBucketDnsRateLimiter>()
        );
        instance.AddHostedService<DnsRateLimitCleanupService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
