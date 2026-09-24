using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive;

/// <summary>
///     This class binds to the <c>DnsServer:Recursive</c> section of the application's
///     configuration and controls whether, and how, <see cref="Resolution.RecursiveQueryResolver" />
///     recurses.
/// </summary>
public class RecursiveServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Recursive";

    /// <summary>
    ///     This property controls whether this server recurses for a query it is not itself
    ///     authoritative for at all. Off by default, the same security-conscious posture as
    ///     every other off-by-default toggle this project has built so far (CHAOS queries,
    ///     dynamic update) -- an open recursive resolver reachable from the public internet is a
    ///     real, well-known abuse vector (DNS amplification), so an operator must opt in
    ///     explicitly.
    /// </summary>
    public bool EnableRecursion { get; set; }

    /// <summary>
    ///     This property controls whether a successfully resolved answer is also validated
    ///     against the DNSSEC chain of trust -- this resolver is a full validating resolver, not
    ///     merely an iterative one. Left independently toggleable from <see cref="EnableRecursion" />
    ///     so an operator who wants faster, non-validating resolution can still have it.
    /// </summary>
    public bool EnableDnssecValidation { get; set; } = true;

    /// <summary>
    ///     This property controls whether <see cref="Resolution.IterativeResolutionEngine" />
    ///     applies RFC 9156 QNAME minimization: rather than sending a query's full name to every
    ///     name server along an iterative referral walk, only the minimum name genuinely needed
    ///     to make progress -- one label deeper than the closest zone cut already known -- is
    ///     revealed to each intermediate server, with the full name (and real query type) sent
    ///     only to the server actually authoritative for it. This reduces how much of a client's
    ///     own query a resolver leaks to every server along the way, a real, if modest, privacy
    ///     improvement with no correctness cost of its own (the RFC's own required fallback -- a
    ///     minimized query that a server does not handle correctly, or an untrustworthy
    ///     intermediate NXDOMAIN, both abandon minimization for the remainder of that one
    ///     resolution and retry with an ordinary, full query -- keeps a non-conformant or
    ///     wildcard-heavy authoritative server from ever breaking a real answer). Defaults to
    ///     <see langword="true" />, the same "on by default" treatment already given to DNSSEC
    ///     validation and RFC 5011 auto-update -- a pure correctness/privacy improvement with no
    ///     corresponding new attack surface, per this project's own confirmed toggle-default
    ///     convention. An operator who hits a real-world authoritative server that mishandles
    ///     minimized queries in a way this resolver's own fallback does not already catch can
    ///     disable it here.
    /// </summary>
    public bool EnableQnameMinimization { get; set; } = true;

    /// <summary>
    ///     This property contains how long a single upstream query may take before this
    ///     resolver gives up on that particular name server and tries the next one.
    /// </summary>
    public TimeSpan UpstreamQueryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property contains the overall time budget for one client-facing query,
    ///     encompassing every referral this resolver follows and every upstream server it tries,
    ///     after which it gives up and answers SERVFAIL.
    /// </summary>
    public TimeSpan OverallQueryTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This property bounds how many referrals (zone cuts followed deeper) a single
    ///     resolution may walk through before giving up with SERVFAIL -- a defense against a
    ///     misconfigured or deliberately malicious delegation loop.
    /// </summary>
    public int MaxReferralDepth { get; set; } = 30;

    /// <summary>
    ///     This property bounds how many CNAME hops a single resolution may follow before giving
    ///     up with SERVFAIL.
    /// </summary>
    public int MaxCnameChainLength { get; set; } = 15;

    /// <summary>
    ///     This property bounds how many upstream queries, in total, a single client-facing
    ///     query may issue -- across every referral, every retried name server, and every
    ///     independent lookup an out-of-bailiwick name server's own address needs -- before
    ///     giving up with SERVFAIL.
    /// </summary>
    public int MaxUpstreamQueries { get; set; } = 100;

    /// <summary>
    ///     This property contains how large the in-memory response cache may grow, in entries,
    ///     before the oldest are evicted to make room for new ones.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 100000;

    /// <summary>
    ///     This property, when non-empty, replaces the hardcoded <see cref="RootHints" /> list
    ///     entirely -- both what <see cref="Resolution.RootHintsPrimingService" /> seeds at
    ///     startup and primes against, and what <see cref="Resolution.IterativeResolutionEngine" />
    ///     falls back to if nothing at all is cached yet. Empty by default, meaning "use the real
    ///     internet root."
    /// </summary>
    public List<RootHintServerOption> RootHintServersOverride { get; set; } = [];

    /// <summary>
    ///     This property, when non-empty, replaces the hardcoded <see cref="RootTrustAnchors" />
    ///     list entirely -- the *initial* seed <see cref="ITrustAnchorStore" /> starts from the
    ///     first time it ever runs (or if its own state file is ever missing or unreadable).
    ///     Once seeded, <see cref="TrustAnchorRolloverService" /> owns keeping this set current if
    ///     <see cref="EnableAutomaticTrustAnchorUpdate" /> is on; this override has no further
    ///     effect after that first seed. Empty by default, meaning "use the real IANA root trust
    ///     anchors."
    /// </summary>
    public List<TrustAnchorOption> TrustAnchorsOverride { get; set; } = [];

    /// <summary>
    ///     This property controls whether this resolver keeps its own trust anchors current
    ///     automatically, per RFC 5011, rather than only ever trusting whatever was hardcoded (or
    ///     configured) at startup. On by default -- unlike the other, abuse-vector-driven
    ///     off-by-default toggles in this class, an out-of-date trust anchor is a pure
    ///     availability/correctness risk with no corresponding abuse concern, and the primary
    ///     deployment this resolver targets (a homelab or home router, set up once and then left
    ///     running for years) is exactly the case that would otherwise silently lose the ability
    ///     to validate anything once the root KSK eventually rolls over, with no operator around
    ///     to notice or intervene.
    /// </summary>
    public bool EnableAutomaticTrustAnchorUpdate { get; set; } = true;

    /// <summary>
    ///     This property contains how often <see cref="TrustAnchorRolloverService" /> actively
    ///     queries the root's own DNSKEY RRset to look for a new or revoked key. RFC 5011 §2.3
    ///     recommends never querying more often than once every 15 days, both to avoid needlessly
    ///     loading the root servers and because a rollover unfolds over months regardless of how
    ///     often it is checked for; this default matches that recommendation exactly.
    /// </summary>
    public TimeSpan TrustAnchorRefreshInterval { get; set; } = TimeSpan.FromDays(15);

    /// <summary>
    ///     This property contains how long a newly observed candidate key must be continuously
    ///     present in the root's own DNSKEY RRset, every time it is checked, before this resolver
    ///     starts trusting it -- RFC 5011 §2.3's add hold-down time, whose recommended (and here,
    ///     default) value is 30 days.
    /// </summary>
    public TimeSpan TrustAnchorHoldDownDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long a previously-<see cref="Validation.TrustAnchorStateEnum.Valid" />
    ///     key may stay <see cref="Validation.TrustAnchorStateEnum.Missing" /> -- absent from a
    ///     target zone's own DNSKEY RRset -- before <see cref="TrustAnchorRolloverService" />
    ///     gives up on it reappearing and transitions it to
    ///     <see cref="Validation.TrustAnchorStateEnum.Removed" />. RFC 5011 §2.3's remove
    ///     hold-down time, whose recommended (and here, default) value is 30 days, the same as
    ///     <see cref="TrustAnchorHoldDownDuration" />'s own add hold-down default -- the RFC
    ///     recommends the same duration for both, but exposes them as independently configurable
    ///     since nothing requires they stay equal.
    /// </summary>
    public TimeSpan TrustAnchorRemoveHoldDownDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains the path <see cref="ITrustAnchorStore" /> persists its own
    ///     state to -- which candidate keys are pending, how long they have been observed for,
    ///     and which are already trusted -- so that state (in particular, a pending key's
    ///     hold-down clock) survives this process restarting, which it otherwise could not.
    ///     Defaults to a <c>data</c> directory alongside the application itself, the same
    ///     convention <c>Ninlil.Dns.Data.EntityFrameworkCore</c>'s own SQLite bootstrap store
    ///     already uses for local, non-database state.
    /// </summary>
    public string TrustAnchorStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "recursive-trust-anchors.json");

    /// <summary>
    ///     This property controls whether <see cref="Resolution.IterativeResolutionEngine" />
    ///     serves an expired-but-still-within-<see cref="MaxStaleServeAge" /> cache entry when a
    ///     fresh answer cannot be obtained (every reachable name server timed out, was
    ///     unreachable, or answered SERVFAIL), per RFC 8767. On by default: a pure availability
    ///     improvement over unconditionally answering SERVFAIL during a genuine upstream outage,
    ///     with no corresponding new attack surface -- the same "self-maintaining capabilities
    ///     default on" posture <see cref="EnableAutomaticTrustAnchorUpdate" /> already follows, and
    ///     particularly apt for this project's own homelab deployment target (a server
    ///     "set up once and left running for years"): an operator who is not watching this
    ///     server closely should see a brief upstream blip degrade to "slightly stale answers,"
    ///     never "every query fails."
    /// </summary>
    public bool EnableServeStale { get; set; } = true;

    /// <summary>
    ///     This property contains how long past its own TTL expiry a cache entry remains eligible
    ///     to be served stale (see <see cref="EnableServeStale" />) before
    ///     <see cref="Caching.RecursiveCacheEvictionService" /> discards it for good. Defaults to
    ///     24 hours -- RFC 8767 §4 recommends a resolver keep expired data available "for a period
    ///     on the order of a day or more" while cautioning against keeping it indefinitely (its own
    ///     illustrative examples cap a stale-serve window at a small number of days); one day is
    ///     this project's own deliberately conservative pick within that range, long enough to ride
    ///     out a typical transient upstream outage without risking a genuinely stale answer
    ///     lingering for very long once real connectivity returns.
    /// </summary>
    public TimeSpan MaxStaleServeAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property controls whether <see cref="Caching.RecursiveCachePrefetchService" />
    ///     proactively refreshes a cache entry that is both nearing TTL expiry
    ///     (<see cref="PrefetchWindow" />) and has genuinely been queried recently
    ///     (<see cref="PrefetchRecentAccessWindow" />), rather than waiting for the next real
    ///     client query to trigger a cold, on-the-critical-path refresh. On by default, for the
    ///     same "pure availability/latency improvement, no new attack surface" reasoning
    ///     <see cref="EnableServeStale" /> already documents -- a popular name's own answer simply
    ///     never goes cold from this resolver's own perspective.
    /// </summary>
    public bool EnablePrefetch { get; set; } = true;

    /// <summary>
    ///     This property contains how close to its own TTL expiry a cache entry must be before
    ///     <see cref="Caching.RecursiveCachePrefetchService" /> considers refreshing it. Defaults
    ///     to 10 seconds -- long enough that a genuinely popular name's refresh reliably completes
    ///     before the original entry actually expires (this project's own default
    ///     <see cref="UpstreamQueryTimeout" /> is 3 seconds per attempt), short enough that this
    ///     service is not constantly re-resolving every cached entry in the whole cache.
    /// </summary>
    public TimeSpan PrefetchWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how recently a cache entry must have actually been looked up
    ///     (via a real client query) to be considered "still being queried," and therefore worth
    ///     prefetching, rather than a name nobody has asked for in a while that merely happens to
    ///     be about to expire. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan PrefetchRecentAccessWindow { get; set; } = TimeSpan.FromMinutes(5);
}
