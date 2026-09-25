using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.RateLimiting;

/// <summary>
///     This interface is query rate limiting per client IP and per subnet.
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> calls it before decoding each query.
///     The implementation, <c>TokenBucketDnsRateLimiter</c> in
///     <c>Ninlil.Dns.Server.RateLimiting</c>, keeps its token buckets in memory, so limits are
///     per node and reset on restart.
/// </summary>
public interface IDnsRateLimiter
{
    /// <summary>
    ///     This method tries to spend one unit of a client's rate-limit budget. It needs only the
    ///     source address, so the pipeline calls it before decoding.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     Whether the query may proceed. <see langword="false" /> means the budget is used up,
    ///     and the pipeline drops the query without a reply.
    /// </returns>
    ValueTask<bool> TryAcquireAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method checks the client's subnet (a CIDR prefix) against a second budget. It
    ///     catches traffic spread over many addresses in one network, which the per-IP budget
    ///     misses because each address stays under its own limit. A query must pass both checks.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="isUdp">
    ///     Whether the query arrived over UDP; only then can the answer be
    ///     <see cref="DnsSubnetRateLimitDecisionEnum.RespondTruncated" />.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The decision for this query.</returns>
    ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
        IPAddress remoteAddress,
        bool isUdp,
        CancellationToken stoppingToken = default
    );
}
