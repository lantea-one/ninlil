using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.RateLimiting;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's per-client-IP rate limiter.
/// </summary>
public static class ServiceCollectionRateLimitingExtension
{
    /// <summary>
    ///     This method registers <see cref="IDnsRateLimiter" /> (backed by
    ///     <see cref="TokenBucketDnsRateLimiter" />) and its background cleanup sweep. A
    ///     composition root that never calls this method leaves
    ///     <see cref="Server.Core.Pipeline.DnsQueryProcessingPipeline" />'s own optional
    ///     <c>rateLimiter</c> constructor parameter unresolved (<see langword="null" />), which
    ///     is exactly equivalent to calling this method with <see cref="DnsRateLimitOptions.Enable" />
    ///     left at its own <see langword="false" /> default.
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
