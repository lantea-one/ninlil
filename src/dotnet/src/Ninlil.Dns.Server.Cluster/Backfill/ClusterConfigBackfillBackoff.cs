using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Backfill;

/// <summary>
///     This class holds the retry rule for a backfill request whose passes keep failing. Without
///     it, a peer that refuses one item (or a pass that cannot finish) would get a full pass at
///     every heartbeat, forever. <see cref="Heartbeat.ClusterHeartbeatBackgroundService" /> asks
///     it before it starts a pass.
///     <para>
///         A request with no failed pass starts at once, so a peer that was only unreachable heals
///         at its first successful heartbeat. After <c>n</c> failed passes (<c>AttemptCount</c>)
///         the next pass starts at <c>LastAttemptAtUtc + min(interval * 2^(n-1), 1 hour)</c>. A new
///         miss only moves <c>RequestedAtUtc</c>, never the count or the attempt time, so it does
///         not shorten or restart the wait. A pass that succeeds removes the request, which resets
///         the count.
///     </para>
/// </summary>
public static class ClusterConfigBackfillBackoff
{
    /// <summary>This field contains the longest wait between two passes to one peer.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);

    /// <summary>
    ///     This method returns when the next pass for a request may start.
    /// </summary>
    /// <param name="request">The peer's request.</param>
    /// <param name="heartbeatInterval">The heartbeat interval, the first wait.</param>
    /// <returns>
    ///     The earliest start time, or <see langword="null" /> when the request may start at once.
    /// </returns>
    public static DateTimeOffset? NextAttemptAtUtc(
        ClusterPeerConfigBackfillEntity request,
        TimeSpan heartbeatInterval
    )
    {
        if (request.AttemptCount <= 0 || request.LastAttemptAtUtc is null)
            return null;

        // The exponent is capped well below overflow; MaxDelay is reached long before it.
        double factor = Math.Pow(2, Math.Min(request.AttemptCount - 1, 30));
        TimeSpan delay = TimeSpan.FromTicks(
            (long)Math.Min(heartbeatInterval.Ticks * factor, MaxDelay.Ticks)
        );
        return request.LastAttemptAtUtc.Value + delay;
    }
}
