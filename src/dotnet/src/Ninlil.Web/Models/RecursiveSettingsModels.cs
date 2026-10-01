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
    TimeSpan PrefetchRecentAccessWindow
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
