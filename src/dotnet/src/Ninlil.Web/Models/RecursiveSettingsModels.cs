using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>RecursiveServerOptions</c>. No secret material is involved, so this record's own fields
///     are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
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
///     This record is the REST representation of one <c>RootHintServerOption</c> entry within
///     <see cref="RecursiveSettingsDetail.RootHintServersOverride" />.
/// </summary>
public record RecursiveRootHintServerDetail(string Name, IReadOnlyList<string> Addresses);

/// <summary>
///     This record is the REST representation of one <c>TrustAnchorOption</c> entry within
///     <see cref="RecursiveSettingsDetail.TrustAnchorsOverride" />.
/// </summary>
public record RecursiveTrustAnchorDetail(
    ushort KeyTag,
    DnssecAlgorithmEnum Algorithm,
    byte DigestType,
    string DigestHex
);
