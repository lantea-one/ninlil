// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This interface is implemented once by <c>Ninlil.Dns.Server.Authoritative</c> and once
///     by <c>Ninlil.Dns.Server.Recursive</c> -- the two engines <see cref="IDnsQueryDispatcher" />
///     tries, in order, for every query. Authoritative and recursive resolution are not two
///     separate servers here; they are two implementations of this one contract, both mounted
///     behind the same set of transports, exactly the way a full-featured DNS server (this
///     server included) is expected to behave: answer authoritatively for a zone this server
///     hosts, and recurse for everything else if recursion is enabled.
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
    ///     example, an authoritative resolver declining a name it hosts no zone for), so the
    ///     dispatcher can offer the query to the next resolver in line.
    /// </returns>
    ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
