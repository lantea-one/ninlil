using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive;

/// <summary>
///     This class binds to the <c>DnsServer:Recursive</c> configuration section and controls
///     recursive resolution, DNSSEC validation, trust anchor rollover and the recursive cache.
///     <c>RecursiveSettingsController</c> edits it at runtime; the resolver, engine, cache,
///     upstream client and background services read it live through
///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, except the root
///     hints priming and the trust anchor store, which read it once at startup. The forwarding
///     resolver also reads the validation and serve-stale settings here.
/// </summary>
public class RecursiveServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Recursive";

    /// <summary>
    ///     This property controls whether this server recurses for names it is not authoritative
    ///     for. It is off by default because an open resolver reachable from the internet is a
    ///     well-known amplification vector, so an operator opts in. An access-control rule can
    ///     still allow recursion for particular clients while this is off.
    /// </summary>
    public bool EnableRecursion { get; set; }

    /// <summary>
    ///     This property controls whether answers are validated against the DNSSEC chain of trust,
    ///     for recursive and forwarded answers alike. It is separate from
    ///     <see cref="EnableRecursion" /> so an operator can choose faster, non-validating
    ///     resolution.
    /// </summary>
    public bool EnableDnssecValidation { get; set; } = true;

    /// <summary>
    ///     This property controls whether <see cref="Resolution.IterativeResolutionEngine" />
    ///     applies RFC 9156 QNAME minimization: each intermediate server sees only one label more
    ///     than the zone cut already known, and only the authoritative server sees the full name
    ///     and real type. This leaks less of the client's query along the way. When a server
    ///     mishandles a minimized query, or returns an NXDOMAIN that cannot be trusted, the
    ///     resolution falls back to full queries (see the engine). It is on by default; turn it
    ///     off for an authoritative server that breaks in a way the fallback does not catch.
    /// </summary>
    public bool EnableQnameMinimization { get; set; } = true;

    /// <summary>
    ///     This property contains how long one upstream query may take before the resolver gives up
    ///     on that name server and tries the next.
    /// </summary>
    public TimeSpan UpstreamQueryTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property contains the time budget for one client query, covering every referral
    ///     and every server tried (see <see cref="Resolution.QueryBudget" />), after which it
    ///     answers SERVFAIL.
    /// </summary>
    public TimeSpan OverallQueryTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This property bounds how many referrals one resolution may follow before giving up with
    ///     SERVFAIL, as a defense against delegation loops.
    /// </summary>
    public int MaxReferralDepth { get; set; } = 30;

    /// <summary>
    ///     This property bounds how many CNAME hops one resolution may follow before giving up
    ///     with SERVFAIL.
    /// </summary>
    public int MaxCnameChainLength { get; set; } = 15;

    /// <summary>
    ///     This property bounds how many upstream queries one client query may issue in total,
    ///     across referrals, retries, name server address lookups and DNSSEC validation, before
    ///     giving up with SERVFAIL.
    /// </summary>
    public int MaxUpstreamQueries { get; set; } = 100;

    /// <summary>
    ///     This property contains how many entries the recursive cache may hold before the
    ///     soonest-to-expire are evicted.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 100000;

    /// <summary>
    ///     This property, when not empty, replaces the built-in <see cref="RootHints" /> list:
    ///     what <see cref="Resolution.RootHintsPrimingService" /> seeds and primes against at
    ///     startup, and what <see cref="Resolution.IterativeResolutionEngine" /> falls back to when
    ///     nothing is cached. Empty by default, meaning the internet root.
    /// </summary>
    public List<RootHintServerOption> RootHintServersOverride { get; set; } = [];

    /// <summary>
    ///     This property, when not empty, replaces the built-in <see cref="RootTrustAnchors" /> as
    ///     the seed <see cref="ITrustAnchorStore" /> starts from when its state file is missing or
    ///     unreadable. Once the file exists this has no effect, and
    ///     <see cref="TrustAnchorRolloverService" /> keeps the set current if
    ///     <see cref="EnableAutomaticTrustAnchorUpdate" /> is on. Empty by default, meaning the IANA
    ///     root anchors.
    /// </summary>
    public List<TrustAnchorOption> TrustAnchorsOverride { get; set; } = [];

    /// <summary>
    ///     This property controls whether the resolver keeps its trust anchors current with
    ///     RFC 5011 rollover (<see cref="TrustAnchorRolloverService" />). It is on by default: an
    ///     outdated anchor is only an availability risk, and a home server left running for years
    ///     would otherwise stop validating after a root KSK rollover.
    /// </summary>
    public bool EnableAutomaticTrustAnchorUpdate { get; set; } = true;

    /// <summary>
    ///     This property contains the longest wait between two checks of
    ///     <see cref="TrustAnchorRolloverService" /> for new or revoked root keys. The service
    ///     checks sooner when RFC 5011 §2.3 says so: the wait is
    ///     MAX(1 hour, MIN(this value, half the DNSKEY signature's Original TTL, half the time
    ///     left until it expires)), which is about one day for the root, and a failed check is
    ///     retried after at most one day. The first check runs one minute after startup. The
    ///     default, 15 days, is the longest wait RFC 5011 §2.3 allows; values below one hour act
    ///     as one hour.
    /// </summary>
    public TimeSpan TrustAnchorRefreshInterval { get; set; } = TimeSpan.FromDays(15);

    /// <summary>
    ///     This property contains how long a new candidate key must be present in the root DNSKEY
    ///     RRset, on every check, before the resolver trusts it: RFC 5011's add hold-down time,
    ///     30 days by default as the RFC recommends.
    /// </summary>
    public TimeSpan TrustAnchorHoldDownDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how long a <see cref="Validation.TrustAnchorStateEnum.Valid" />
    ///     key may stay <see cref="Validation.TrustAnchorStateEnum.Missing" /> (absent from the root
    ///     DNSKEY RRset) before <see cref="TrustAnchorRolloverService" /> marks it
    ///     <see cref="Validation.TrustAnchorStateEnum.Removed" />. It is modeled on RFC 5011's
    ///     remove hold-down time and defaults to 30 days, like
    ///     <see cref="TrustAnchorHoldDownDuration" />.
    /// </summary>
    public TimeSpan TrustAnchorRemoveHoldDownDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains the file <see cref="ITrustAnchorStore" /> persists the trust
    ///     anchor state to (which keys are pending, since when, and which are trusted), so a
    ///     pending key's hold-down clock survives a restart. It defaults to a <c>data</c> directory
    ///     next to the application.
    /// </summary>
    public string TrustAnchorStateFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "recursive-trust-anchors.json");

    /// <summary>
    ///     This property controls RFC 8767 serve-stale: when a fresh answer cannot be obtained
    ///     (every name server timed out, was unreachable or answered SERVFAIL),
    ///     <see cref="Resolution.IterativeResolutionEngine" /> serves an expired cache entry that is
    ///     within <see cref="MaxStaleServeAge" />. The forwarding resolver follows the same
    ///     setting. It is on by default, so a brief upstream outage gives slightly stale answers
    ///     rather than failures.
    /// </summary>
    public bool EnableServeStale { get; set; } = true;

    /// <summary>
    ///     This property contains how long past expiry a cache entry may still be served stale
    ///     (see <see cref="EnableServeStale" />) before
    ///     <c>Ninlil.Dns.Server.Core.Caching.DnsCacheEvictionService</c> discards it. The default
    ///     is 24 hours, at the short end of the one-to-seven-day range RFC 8767 §4 suggests: long
    ///     enough to ride out a typical outage without keeping stale data for long.
    /// </summary>
    public TimeSpan MaxStaleServeAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    ///     This property controls whether <see cref="Caching.RecursiveCachePrefetchService" />
    ///     refreshes a positive cache entry that is near expiry (<see cref="PrefetchWindow" />) and
    ///     was looked up recently (<see cref="PrefetchRecentAccessWindow" />), so a popular name is
    ///     refreshed before a client has to wait for it. On by default.
    /// </summary>
    public bool EnablePrefetch { get; set; } = true;

    /// <summary>
    ///     This property contains how close to expiry a cache entry must be before
    ///     <see cref="Caching.RecursiveCachePrefetchService" /> considers refreshing it. The default,
    ///     10 seconds, leaves time for a refresh (the per-server timeout is 3 seconds) without
    ///     refreshing entries long before they expire.
    /// </summary>
    public TimeSpan PrefetchWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how recently a cache entry must have been returned by a lookup
    ///     to be worth prefetching, so names nobody asks for are left to expire. Lookups include
    ///     the engine's internal ones. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan PrefetchRecentAccessWindow { get; set; } = TimeSpan.FromMinutes(5);
}
