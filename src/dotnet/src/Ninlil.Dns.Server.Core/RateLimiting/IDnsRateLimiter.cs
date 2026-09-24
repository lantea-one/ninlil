using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.RateLimiting;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for
///     per-client-IP rate limiting, implemented by <c>Ninlil.Dns.Server.RateLimiting</c> and
///     consulted by <see cref="Pipeline.DnsQueryProcessingPipeline" /> -- the same "define the
///     abstraction here, implement it in a dedicated project" shape
///     <see cref="Filtering.IDnsFilterEngine" /> already establishes, even though this
///     particular implementation needs no storage of its own (in-memory-only token buckets, by
///     the user's own explicit choice for this project's single-instance homelab deployment
///     target).
/// </summary>
public interface IDnsRateLimiter
{
    /// <summary>
    ///     This method attempts to spend one unit of a querying client's own rate-limit budget.
    ///     Called before a query is even decoded, exactly like
    ///     <see cref="Filtering.IDnsFilterEngine.EvaluateClientAsync" />, since a budget decision
    ///     needs only the client's own source address.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether the query may proceed. A
    ///     <see langword="false" /> result means the client's budget is currently exhausted; the
    ///     query is silently dropped, the same "don't confirm this server's own existence to a
    ///     client that shouldn't be talking to it this fast" reasoning already applied to a
    ///     filter-denied client.
    /// </returns>
    ValueTask<bool> TryAcquireAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method checks a querying client's own subnet against a second, independent
    ///     rate-limit layer bucketed by CIDR prefix rather than exact IP -- a coarser check aimed
    ///     at a burst spread across many source addresses within the same network (a real,
    ///     historically exploited DNS amplification/reflection shape a per-exact-IP check alone
    ///     cannot catch, since each individual spoofed source address may only ever be seen once)
    ///     rather than <see cref="TryAcquireAsync" />'s own steady-state, single-client budget.
    ///     Checked independently of, and in addition to, <see cref="TryAcquireAsync" /> -- a query
    ///     must pass both to proceed.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="isUdp">
    ///     Whether this query arrived over UDP -- see <see cref="DnsSubnetRateLimitDecisionEnum.RespondTruncated" />'s
    ///     own doc comment for why this decides whether an over-limit query can even be answered
    ///     with a "retry over TCP" signal at all.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>This subnet's own current decision.</returns>
    ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
        IPAddress remoteAddress,
        bool isUdp,
        CancellationToken stoppingToken = default
    );
}
