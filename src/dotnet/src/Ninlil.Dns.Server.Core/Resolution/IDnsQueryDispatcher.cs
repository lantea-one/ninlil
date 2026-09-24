using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This interface is the single entry point every transport calls to get a query answered.
///     It hides, from a transport's perspective, that there might be an authoritative engine, a
///     recursive engine, both, or neither, behind it -- a transport only ever needs to decode
///     bytes into a query, hand it here, and encode whatever comes back.
/// </summary>
public interface IDnsQueryDispatcher
{
    /// <summary>
    ///     This method resolves a single query.
    /// </summary>
    /// <param name="context">The query to resolve.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A complete answer, ready to be encoded and sent back to the requestor.</returns>
    ValueTask<DnsMessage> DispatchAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
