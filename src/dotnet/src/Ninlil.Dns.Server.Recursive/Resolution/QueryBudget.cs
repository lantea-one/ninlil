// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class tracks two bounds across a whole client query, including nested lookups for
///     name server addresses and DNSSEC validation: an overall deadline
///     (<see cref="RecursiveServerOptions.OverallQueryTimeout" />) and a total number of upstream
///     queries (<see cref="RecursiveServerOptions.MaxUpstreamQueries" />). They keep a hostile or
///     broken delegation from making one query do unbounded work. The deadline starts when the
///     budget is created.
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
