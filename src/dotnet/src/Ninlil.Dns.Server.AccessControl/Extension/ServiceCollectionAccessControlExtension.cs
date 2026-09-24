using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's Phase 5o-10 real per-client/network access-control engine.
/// </summary>
public static class ServiceCollectionAccessControlExtension
{
    /// <summary>
    ///     This method registers the access-control rule cache, its own change-driven
    ///     invalidation subscriber, and the engine that answers every real capability check from
    ///     it -- mirroring exactly how <c>ServiceCollectionFilteringExtension</c> registers
    ///     <c>DnsFilterEngine</c> and how <c>ServiceCollectionAuthoritativeExtension</c> registers
    ///     <c>AuthoritativeZoneCache</c>/<c>ZoneChangedEventSubscriber</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerAccessControl(
        this IServiceCollection instance
    )
    {
        // DnsAccessControlRuleCache is registered once, as a singleton, under both its concrete
        // type (which DnsAccessControlEngine and DnsAccessControlRuleChangedEventSubscriber
        // depend on directly) and IHostedService (so the host starts it, triggering its initial
        // full load) -- the exact same "one singleton, two registrations" shape
        // AuthoritativeZoneCache already uses.
        instance.AddSingleton<DnsAccessControlRuleCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<DnsAccessControlRuleCache>()
        );

        // Singleton, not scoped: RecursiveQueryResolver and TokenBucketDnsRateLimiter (both
        // themselves singletons) take IDnsAccessControlEngine as a direct constructor dependency,
        // so a scoped registration here would be an invalid captive dependency. The engine itself
        // never opens a repository directly -- it only ever reads DnsAccessControlRuleCache's
        // already-warm in-memory snapshot.
        instance.AddSingleton<IDnsAccessControlEngine, DnsAccessControlEngine>();

        // Registered against DnsAccessControlRuleCache's own singleton instance too, the same
        // "scoped subscriber over a singleton dependency" shape ZoneChangedEventSubscriber uses,
        // since DnsServerEventDispatcher always resolves subscribers through a fresh scope per
        // event regardless of what they themselves depend on.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsAccessControlRuleChangedEvent>,
            DnsAccessControlRuleChangedEventSubscriber
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
