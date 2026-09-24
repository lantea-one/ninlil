// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class tracks the two bounds <see cref="IterativeResolutionEngine" /> enforces across
///     an *entire* client-facing query -- including every nested lookup needed to resolve a name
///     server's own address -- rather than per referral level: an overall deadline, and a total
///     count of upstream queries issued -- iterative resolution bounds rather than trusts the
///     network, the same posture the authoritative side's own delegation-loop protection takes.
/// </summary>
/// <param name="options">The configured bounds to enforce.</param>
public sealed class QueryBudget(RecursiveServerOptions options)
{
    private readonly DateTimeOffset _deadlineUtc =
        DateTimeOffset.UtcNow + options.OverallQueryTimeout;
    private int _queriesIssued;

    /// <summary>
    ///     This method reports whether one more upstream query may still be issued, counting it
    ///     against the budget if so.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if a query may proceed, or <see langword="false" /> if the
    ///     overall deadline has passed or the maximum query count has already been reached.
    /// </returns>
    public bool TryConsumeQuery()
    {
        if (DateTimeOffset.UtcNow >= _deadlineUtc)
            return false;

        return Interlocked.Increment(ref _queriesIssued) <= options.MaxUpstreamQueries;
    }
}
