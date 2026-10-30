using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This record is the JSON payload of a <c>DnsFilterListEntity</c> config-sync envelope:
///     every editable field except <c>LastFetchedAtUtc</c>, which records when this node last
///     fetched a subscribed list and so stays local. <c>AppliesToAllClients</c> defaults to
///     <see langword="false" />, so an envelope from a node without the field reads as a list
///     that applies only through assignments.
/// </summary>
public sealed record FilterListSyncPayload(
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    DnsFilterListSourceEnum Source,
    string SubscriptionUrl,
    TimeSpan? RefreshInterval,
    bool IsDisabled,
    string Comments,
    bool AppliesToAllClients = false
);
