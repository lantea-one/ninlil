using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record describes one subscribed block or allow list that a preset attaches to the
///     default client group. <see cref="PresetFilterListApplier" /> turns it into a
///     <c>DnsFilterListEntity</c>, which <c>DnsFilterListExternalRefreshService</c> then fetches
///     and refreshes like any other subscribed list.
/// </summary>
/// <param name="Name">
///     A stable, preset-owned name (for example <c>"Ninlil Preset: Protective Threat Feed"</c>)
///     used both as the persisted <c>DnsFilterListEntity.Name</c> and as the idempotency key
///     <see cref="PresetFilterListApplier" /> matches on when a preset is applied more than once.
/// </param>
/// <param name="Kind">The kind of list; every built-in preset list is a domain list.</param>
/// <param name="Action">Whether the list's entries are blocked or allowed.</param>
/// <param name="SubscriptionUrl">The URL this list's entries are fetched from (hosts-file, bare-domain or adblock syntax).</param>
/// <param name="RefreshInterval">How often the subscribed URL should be refetched.</param>
public sealed record PresetFilterListSubscription(
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    string SubscriptionUrl,
    TimeSpan RefreshInterval
);
