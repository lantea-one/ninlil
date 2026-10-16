using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the recursive resolver settings
///     (<c>RecursiveServerOptions</c>) that <c>GET /api/recursive-settings</c> returns. A
///     <c>PATCH</c> to the same endpoint accepts the same fields. The two override lists are read
///     once at startup or first seed, so the GET shows the saved lists, which may not be the ones
///     in use; every other field takes effect at once. <c>RecursiveServerOptions</c> documents
///     each field and its default.
///     <para>
///         <c>InsecureDomains</c> is the list of Insecure domains (negative trust anchors) in
///         force: a query for a name at or below one skips DNSSEC validation and is never answered
///         with AD. It is the built-in list while <c>UseCustomInsecureDomains</c> is
///         <see langword="false" />. A <c>PATCH</c> with an array replaces the whole list and makes
///         it custom; a <c>PATCH</c> with <see langword="null" /> restores the built-in list.
///         <c>BuiltInInsecureDomains</c> is that built-in list (<c>home.arpa</c>, <c>internal</c>
///         and the locally-served reverse zones of RFC 6303), for comparison and for a "restore
///         defaults" action; it is read-only.
///     </para>
///     <para>
///         <c>EnableRecursion</c> turns on recursive service for every client, by iterating from
///         the root and by forwarding to the configured forwarders (forwarding also needs its own
///         switch). While it is off, an access rule granting recursion access still lets
///         particular clients use both.
///     </para>
/// </summary>
public record RecursiveSettingsDetail(
    bool EnableRecursion,
    bool EnableDnssecValidation,
    bool EnableQnameMinimization,
    TimeSpan UpstreamQueryTimeout,
    TimeSpan OverallQueryTimeout,
    int MaxReferralDepth,
    int MaxCnameChainLength,
    int MaxUpstreamQueries,
    int MaxCacheEntries,
    IReadOnlyList<RecursiveRootHintServerDetail> RootHintServersOverride,
    IReadOnlyList<RecursiveTrustAnchorDetail> TrustAnchorsOverride,
    bool EnableAutomaticTrustAnchorUpdate,
    TimeSpan TrustAnchorRefreshInterval,
    TimeSpan TrustAnchorHoldDownDuration,
    TimeSpan TrustAnchorRemoveHoldDownDuration,
    bool EnableServeStale,
    TimeSpan MaxStaleServeAge,
    bool EnablePrefetch,
    TimeSpan PrefetchWindow,
    TimeSpan PrefetchRecentAccessWindow,
    IReadOnlyList<string> InsecureDomains,
    bool UseCustomInsecureDomains,
    IReadOnlyList<string> BuiltInInsecureDomains
);

/// <summary>
///     This record is one <c>RootHintServerOption</c> entry in
///     <see cref="RecursiveSettingsDetail.RootHintServersOverride" />: a root server's name and
///     addresses. An empty override list uses the built-in internet root hints.
/// </summary>
public record RecursiveRootHintServerDetail(string Name, IReadOnlyList<string> Addresses);

/// <summary>
///     This record is one <c>TrustAnchorOption</c> entry in
///     <see cref="RecursiveSettingsDetail.TrustAnchorsOverride" />: a root DS record given as key
///     tag, algorithm, digest type and hex digest. An empty override list uses the IANA root
///     anchors. The list only seeds the trust anchor store when its state file is missing.
/// </summary>
public record RecursiveTrustAnchorDetail(
    ushort KeyTag,
    DnssecAlgorithmEnum Algorithm,
    byte DigestType,
    string DigestHex
);
