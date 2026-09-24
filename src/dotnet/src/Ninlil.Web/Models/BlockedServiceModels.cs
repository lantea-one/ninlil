// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a single curated "blocked service" catalog entry's own metadata,
///     together with whether it is currently enabled on this server -- the response shape for
///     <c>GET /api/blocked-services</c>.
/// </summary>
/// <param name="Key">The catalog entry's own stable identifier (e.g. <c>tiktok</c>).</param>
/// <param name="DisplayName">The human-readable service name.</param>
/// <param name="Description">A short, honest description of what enabling this entry blocks.</param>
/// <param name="SubscriptionUrl">
///     The real, third-party-hosted domain list this entry subscribes to when enabled -- see
///     <c>BlockedServiceCatalog</c>'s own doc comment for its source and license.
/// </param>
/// <param name="IsEnabled">
///     Whether this server currently has a managed, subscribed filter list for this entry.
/// </param>
/// <param name="FilterListId">
///     The identifier of this entry's own managed <c>DnsFilterListEntity</c>, when
///     <see cref="IsEnabled" /> is <see langword="true" /> (so an operator can jump straight to it
///     in the ordinary Filter Lists page, e.g. to inspect its currently-fetched entries); otherwise
///     <see langword="null" />.
/// </param>
public record BlockedServiceDetail(
    string Key,
    string DisplayName,
    string Description,
    string SubscriptionUrl,
    bool IsEnabled,
    Guid? FilterListId
);
