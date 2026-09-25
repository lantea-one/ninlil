using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Transport;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the
///     recursive DNS resolver.
/// </summary>
public static class ServiceCollectionRecursiveExtension
{
    /// <summary>
    ///     This method binds <see cref="RecursiveServerOptions" /> and registers the cache, prefetch,
    ///     upstream client, iterative engine, root-hints priming, trust anchor store and rollover,
    ///     DNSSEC validator, and <see cref="RecursiveQueryResolver" /> as an
    ///     <see cref="IDnsQueryResolver" />. The caller must already have called
    ///     <c>AddNinlilDnsServerCore</c>, and must call this last among the resolvers (after the
    ///     authoritative and forwarding ones), because the dispatcher tries resolvers in
    ///     registration order and recursion is the final fallback.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read recursive settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerRecursive(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<RecursiveServerOptions>(
            configuration.GetSection(RecursiveServerOptions.SectionName)
        );

        // A singleton, so the name servers, glue and answers one resolution learns serve every
        // later query.
        instance.AddSingleton<RecursiveCache>();
        instance.AddSingleton<IRecursiveCache>(provider =>
            provider.GetRequiredService<RecursiveCache>()
        );

        // The same instance is also an IDnsCache, so DnsCacheEvictionService sweeps it and the
        // cache REST endpoint reports and flushes it.
        instance.AddSingleton<IDnsCache>(provider => provider.GetRequiredService<RecursiveCache>());
        instance.AddHostedService<RecursiveCachePrefetchService>();

        instance.AddSingleton<IUpstreamDnsClient, UpstreamDnsClient>();
        instance.AddSingleton<IIterativeResolutionEngine, IterativeResolutionEngine>();
        instance.AddHostedService<RootHintsPrimingService>();

        // A singleton, since the RFC 5011 state is shared by every query and every check.
        instance.AddSingleton<ITrustAnchorStore, FileTrustAnchorStore>();
        instance.AddHostedService<TrustAnchorRolloverService>();

        instance.AddSingleton<IDnssecValidator, DnssecValidator>();
        instance.AddSingleton<IDnsQueryResolver, RecursiveQueryResolver>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
