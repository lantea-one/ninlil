using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class provides the <see cref="IServiceCollection" /> extension that registers the
///     read router and the per-scope read intent. Both storage extensions
///     (<c>AddNinlilDnsEntityFrameworkCore</c> and <c>AddNinlilDnsMongoDb</c>) call it, so the
///     registrations exist whichever engine is configured.
/// </summary>
public static class ServiceCollectionStorageRoutingExtension
{
    /// <summary>
    ///     This method registers <see cref="IStorageReadRouter" /> (one per process, disabled until
    ///     <see cref="IStorageReadRouter.Enable" />) and the scoped <see cref="StorageReadIntent" />
    ///     and <see cref="StoragePrincipal" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="splitConfigured">Whether the storage settings describe a read/write split.</param>
    /// <param name="stickyWindow">
    ///     How long a principal reads the primary after it wrote
    ///     (<c>Storage:ReadReplica:StickyAfterWriteSeconds</c>).
    /// </param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilStorageRouting(
        this IServiceCollection instance,
        bool splitConfigured,
        TimeSpan stickyWindow
    )
    {
        instance.TryAddSingleton<IStorageReadRouter>(
            new StorageReadRouter(splitConfigured, stickyWindow)
        );
        instance.TryAddScoped<StorageReadIntent>();
        instance.TryAddScoped<StoragePrincipal>();
        return instance;
    }

    /// <summary>
    ///     This method registers the <see cref="ReplicaHealthMonitor" /> as a hosted service,
    ///     with the probe that <paramref name="probeFactory" /> builds. A storage extension calls
    ///     it only when the settings describe a split, so without a replica no monitor runs.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="probeFactory">Builds the engine-specific probe.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilReplicaHealthMonitor(
        this IServiceCollection instance,
        Func<IServiceProvider, IReplicaHealthProbe> probeFactory
    )
    {
        instance.TryAddSingleton(probeFactory);
        instance.TryAddSingleton<ReplicaHealthMonitor>();
        instance.AddHostedService(provider => provider.GetRequiredService<ReplicaHealthMonitor>());
        return instance;
    }
}
