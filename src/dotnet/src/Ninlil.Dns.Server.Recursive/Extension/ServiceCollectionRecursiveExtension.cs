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
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's recursive DNS engine.
/// </summary>
public static class ServiceCollectionRecursiveExtension
{
    /// <summary>
    ///     This method registers everything <c>Ninlil.Dns.Server.Recursive</c> needs. The
    ///     composition root is still expected to have already called
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>,
    ///     and to register this resolver *after* <c>Ninlil.Dns.Server.Authoritative</c>'s own,
    ///     since an authoritative answer always wins regardless of the RD bit.
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

        // IRecursiveCache is a singleton -- an iterative resolution's whole point is to build up
        // shared knowledge (name servers, glue, answers) that every future query benefits from,
        // not something scoped to a single request.
        instance.AddSingleton<RecursiveCache>();
        instance.AddSingleton<IRecursiveCache>(provider =>
            provider.GetRequiredService<RecursiveCache>()
        );

        // The same instance is also an IDnsCache, which is how Server.Core's own
        // DnsCacheEvictionService sweeps it and the cache REST endpoint reports and flushes it.
        instance.AddSingleton<IDnsCache>(provider => provider.GetRequiredService<RecursiveCache>());
        instance.AddHostedService<RecursiveCachePrefetchService>();

        instance.AddSingleton<IUpstreamDnsClient, UpstreamDnsClient>();
        instance.AddSingleton<IIterativeResolutionEngine, IterativeResolutionEngine>();
        instance.AddHostedService<RootHintsPrimingService>();

        // ITrustAnchorStore is a singleton for the same reason IRecursiveCache is: its whole
        // point (durable RFC 5011 rollover state) only makes sense shared across every query and
        // every check, never scoped to one.
        instance.AddSingleton<ITrustAnchorStore, FileTrustAnchorStore>();
        instance.AddHostedService<TrustAnchorRolloverService>();

        instance.AddSingleton<IDnssecValidator, DnssecValidator>();
        instance.AddSingleton<IDnsQueryResolver, RecursiveQueryResolver>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
