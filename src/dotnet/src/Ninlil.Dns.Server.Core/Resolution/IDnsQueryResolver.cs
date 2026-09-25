// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This interface is one engine that can answer a query. <see cref="DnsQueryDispatcher" />
///     offers each query to the registered resolvers in order until one claims it. The
///     implementations are, in order: <c>NotifyQueryResolver</c>, <c>DynamicUpdateQueryResolver</c>
///     and <c>AuthoritativeQueryResolver</c> (Server.Authoritative), <c>ForwarderQueryResolver</c>
///     (Server.Forwarding) and <c>RecursiveQueryResolver</c> (Server.Recursive). All of them sit
///     behind the same transports and <see cref="Pipeline.DnsQueryProcessingPipeline" />.
/// </summary>
public interface IDnsQueryResolver
{
    /// <summary>
    ///     This method attempts to resolve a single query.
    /// </summary>
    /// <param name="context">The query to attempt to resolve.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see cref="DnsQueryResolution.Handled" /> with a complete answer if this resolver
    ///     claims the query, or <see cref="DnsQueryResolution.NotHandled" /> if it does not (for
    ///     example, the authoritative resolver for a name in no hosted zone), so the dispatcher can
    ///     offer the query to the next resolver.
    /// </returns>
    ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
