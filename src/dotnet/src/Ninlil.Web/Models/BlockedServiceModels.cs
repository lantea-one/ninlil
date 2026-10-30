// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries one entry of the curated "blocked services" catalog, with whether it is
///     enabled. It is the response shape of <c>GET /api/blocked-services</c>.
/// </summary>
/// <param name="Key">The catalog entry's stable identifier (e.g. <c>tiktok</c>).</param>
/// <param name="DisplayName">The human-readable service name.</param>
/// <param name="Description">A short description of what enabling this entry blocks.</param>
/// <param name="SubscriptionUrl">
///     The third-party domain list this entry subscribes to when enabled.
///     <c>BlockedServiceCatalog</c> documents its source and license.
/// </param>
/// <param name="IsEnabled">
///     Whether the service is blocked for every client: the entry's managed filter list is
///     enabled and applies to all clients. When the request names a client or client group, it is
///     whether that target has an assignment of the enabled list instead.
/// </param>
/// <param name="FilterListId">
///     The identifier of the entry's managed <c>DnsFilterListEntity</c> when one exists, so the UI
///     can link to it on the Filter Lists page; otherwise <see langword="null" />.
/// </param>
public record BlockedServiceDetail(
    string Key,
    string DisplayName,
    string Description,
    string SubscriptionUrl,
    bool IsEnabled,
    Guid? FilterListId
);
