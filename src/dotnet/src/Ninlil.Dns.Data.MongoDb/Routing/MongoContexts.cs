// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Routing;

/// <summary>
///     This class holds the two <see cref="NinlilDnsMongoContext" /> instances of the process,
///     created once because the contexts are thread-safe: the primary context and, when
///     <c>Storage:MongoDb:ReadPreference</c> asks for a split, the read-only replica view. The
///     scoped registration of <see cref="NinlilDnsMongoContext" /> in
///     <see cref="Extension.ServiceCollectionMongoDbExtension" /> picks one per scope.
/// </summary>
/// <param name="primary">The context for writes and for work that needs the primary.</param>
/// <param name="replica">The read-only view, or null when there is no split.</param>
public sealed class MongoContexts(NinlilDnsMongoContext primary, NinlilDnsMongoContext replica)
{
    /// <summary>
    ///     This property contains the context for writes and for work that needs the primary.
    /// </summary>
    public NinlilDnsMongoContext Primary { get; } = primary;

    /// <summary>
    ///     This property contains the read-only replica view, or null when there is no split.
    /// </summary>
    public NinlilDnsMongoContext Replica { get; } = replica;
}
