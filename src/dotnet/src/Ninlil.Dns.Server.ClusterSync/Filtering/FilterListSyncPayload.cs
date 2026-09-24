using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This record is the JSON shape a <c>DnsFilterListEntity</c>'s own config-sync envelope
///     carries -- every mutable field except <c>LastFetchedAtUtc</c>, which is deliberately
///     excluded: it records when <em>this</em> node last refetched a subscribed list's own
///     entries, a purely local operational fact each node tracks independently, not something a
///     cluster-wide sync should ever overwrite with a different node's own fetch time.
/// </summary>
public sealed record FilterListSyncPayload(
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    DnsFilterListSourceEnum Source,
    string SubscriptionUrl,
    TimeSpan? RefreshInterval,
    bool IsDisabled,
    string Comments
);
