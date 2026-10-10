using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    ///     <see cref="IStorageReadRouter.Enable" />) and the scoped <see cref="StorageReadIntent" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="splitConfigured">Whether the storage settings describe a read/write split.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilStorageRouting(
        this IServiceCollection instance,
        bool splitConfigured
    )
    {
        instance.TryAddSingleton<IStorageReadRouter>(new StorageReadRouter(splitConfigured));
        instance.TryAddScoped<StorageReadIntent>();
        return instance;
    }
}
