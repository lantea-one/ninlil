using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up the
///     per-network access-control engine (recursion access and rate-limit bypass rules).
/// </summary>
public static class ServiceCollectionAccessControlExtension
{
    /// <summary>
    ///     This method registers the access-control rule cache, the event subscriber that reloads
    ///     it when a rule changes, and the engine that answers capability checks from it. The
    ///     cache and subscriber are registered the same way as <c>AuthoritativeZoneCache</c> and
    ///     <c>ZoneChangedEventSubscriber</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerAccessControl(
        this IServiceCollection instance
    )
    {
        // One singleton, registered under its concrete type (which DnsAccessControlEngine and
        // DnsAccessControlRuleChangedEventSubscriber depend on) and as IHostedService (so the
        // host starts it and runs the initial load), as AuthoritativeZoneCache is.
        instance.AddSingleton<DnsAccessControlRuleCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<DnsAccessControlRuleCache>()
        );

        // Singleton, not scoped: RecursiveQueryResolver and TokenBucketDnsRateLimiter are
        // singletons that take IDnsAccessControlEngine in their constructors, so a scoped
        // registration would be a captive dependency. The engine never opens a repository; it
        // reads only the cache's in-memory snapshot.
        instance.AddSingleton<IDnsAccessControlEngine, DnsAccessControlEngine>();

        // Scoped, because DnsServerEventDispatcher resolves subscribers in a fresh scope per
        // event; it still receives the singleton cache, as ZoneChangedEventSubscriber does.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsAccessControlRuleChangedEvent>,
            DnsAccessControlRuleChangedEventSubscriber
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
