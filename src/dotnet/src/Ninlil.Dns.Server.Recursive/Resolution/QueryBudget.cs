using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class tracks the bounds across a whole client query, including nested lookups for
///     name server addresses and DNSSEC validation: an overall deadline
///     (<see cref="RecursiveServerOptions.OverallQueryTimeout" />), a total number of upstream
///     queries (<see cref="RecursiveServerOptions.MaxUpstreamQueries" />), a total number of
///     resolution steps, and the name server address lookups in progress. They keep a hostile or
///     broken delegation from making one query do unbounded work. The deadline starts when the
///     budget is created.
///     <para>
///         Steps and name server lookups exist because a cache-answered step sends no query: a
///         glueless delegation whose name servers lie inside it (after their glue expired), or
///         an unprimed cache whose root servers need the root to be found, would otherwise
///         recurse without spending any query until the stack overflows.
///     </para>
/// </summary>
/// <param name="options">The configured bounds to enforce.</param>
public sealed class QueryBudget(RecursiveServerOptions options)
{
    private readonly DateTimeOffset _deadlineUtc =
        DateTimeOffset.UtcNow + options.OverallQueryTimeout;
    private readonly HashSet<DnsName> _nameServerLookupsInProgress = [];
    private int _queriesIssued;
    private int _stepsTaken;

    /// <summary>
    ///     This constant bounds how many resolution steps (each name and type the engine resolves,
    ///     whether the cache answers it or not) one budget allows. A normal resolution with
    ///     DNSSEC validation takes a few dozen.
    /// </summary>
    public const int MaxResolutionSteps = 1000;

    /// <summary>
    ///     This constant bounds how deeply name server address lookups may nest: a lookup for a
    ///     name server's address that itself needs another name server's address, and so on.
    /// </summary>
    public const int MaxNameServerLookupNesting = 8;

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

    /// <summary>
    ///     This method reports whether one more resolution step may still be taken, counting it
    ///     against the budget if so. Unlike <see cref="TryConsumeQuery" /> it counts steps the
    ///     cache answers too.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the step may proceed, or <see langword="false" /> if the
    ///     overall deadline has passed or <see cref="MaxResolutionSteps" /> has been reached.
    /// </returns>
    public bool TryConsumeStep()
    {
        if (DateTimeOffset.UtcNow >= _deadlineUtc)
            return false;

        return Interlocked.Increment(ref _stepsTaken) <= MaxResolutionSteps;
    }

    /// <summary>
    ///     This method marks the start of an address lookup for a name server, unless that would
    ///     loop or nest too deeply. Every successful call must be paired with
    ///     <see cref="EndNameServerLookup" />.
    /// </summary>
    /// <param name="nameServer">The name server whose address is about to be looked up.</param>
    /// <returns>
    ///     <see langword="true" /> if the lookup may proceed, or <see langword="false" /> if the
    ///     same name server's address is already being looked up further up this resolution, or
    ///     <see cref="MaxNameServerLookupNesting" /> lookups are already in progress.
    /// </returns>
    public bool TryBeginNameServerLookup(DnsName nameServer)
    {
        lock (_nameServerLookupsInProgress)
        {
            if (_nameServerLookupsInProgress.Count >= MaxNameServerLookupNesting)
                return false;

            return _nameServerLookupsInProgress.Add(nameServer);
        }
    }

    /// <summary>
    ///     This method marks the end of an address lookup started with
    ///     <see cref="TryBeginNameServerLookup" />.
    /// </summary>
    /// <param name="nameServer">The name server whose address lookup has finished.</param>
    public void EndNameServerLookup(DnsName nameServer)
    {
        lock (_nameServerLookupsInProgress)
            _nameServerLookupsInProgress.Remove(nameServer);
    }
}
