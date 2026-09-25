using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This interface resolves a decoded query to a response by offering it to the registered
///     <see cref="IDnsQueryResolver" />s. <see cref="Pipeline.DnsQueryProcessingPipeline" /> calls
///     it after its access, cookie, schedule, filter and rewrite checks; the transports never call
///     it directly. The implementation is <see cref="DnsQueryDispatcher" />.
/// </summary>
public interface IDnsQueryDispatcher
{
    /// <summary>
    ///     This method resolves a single query.
    /// </summary>
    /// <param name="context">The query to resolve.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A complete answer, ready to be encoded and sent to the client.</returns>
    ValueTask<DnsMessage> DispatchAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );
}
