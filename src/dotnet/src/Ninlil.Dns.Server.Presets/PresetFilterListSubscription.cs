using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record describes a single subscribed block/allow list a preset attaches to the
///     default client group -- <see cref="PresetFilterListApplier" />'s own unit of work,
///     matching the exact shape <c>DnsFilterListEntity</c> and
///     <c>DnsFilterListExternalRefreshService</c>'s own hosts-file-format refresh mechanism
///     already expect.
/// </summary>
/// <param name="Name">
///     A stable, preset-owned name (for example <c>"Ninlil Preset: Protective Threat Feed"</c>)
///     used both as the persisted <c>DnsFilterListEntity.Name</c> and as the idempotency key
///     <see cref="PresetFilterListApplier" /> matches on when a preset is applied more than once.
/// </param>
/// <param name="Kind">The kind of list this is -- every preset-owned list is a domain block list.</param>
/// <param name="Action">Whether this list's own entries are blocked or allowed.</param>
/// <param name="SubscriptionUrl">The hosts-file-format URL this list's entries are fetched from.</param>
/// <param name="RefreshInterval">How often the subscribed URL should be refetched.</param>
public sealed record PresetFilterListSubscription(
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    string SubscriptionUrl,
    TimeSpan RefreshInterval
);
