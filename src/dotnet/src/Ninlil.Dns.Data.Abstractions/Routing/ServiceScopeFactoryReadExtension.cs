using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class provides the <see cref="IServiceScopeFactory" /> extension that background code
///     uses to read from a replica. A caller that only reads (a cache refresh that tolerates a
///     little lag, for example) opens its scope here instead of with
///     <c>CreateAsyncScope</c>; everything it resolves in the scope then reads from the replica,
///     when the router allows it.
/// </summary>
public static class ServiceScopeFactoryReadExtension
{
    /// <summary>
    ///     This method opens a scope whose <see cref="StorageReadIntent" /> is
    ///     <see cref="StorageReadTargetEnum.Replica" />. Writing in such a scope throws.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceScopeFactory" /> instance.</param>
    /// <returns>The new scope; dispose it when the read is done.</returns>
    public static AsyncServiceScope CreateReplicaReadScope(this IServiceScopeFactory instance)
    {
        AsyncServiceScope scope = instance.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<StorageReadIntent>()
            .Set(StorageReadTargetEnum.Replica);
        return scope;
    }
}
