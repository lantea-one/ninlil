using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a block or allow list
///     (<c>POST /api/filter-lists</c>, <c>PUT /api/filter-lists/{id}</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the list.</param>
/// <param name="Kind">What kind of value the list's entries hold.</param>
/// <param name="Action">Whether a match against the list blocks or allows.</param>
/// <param name="Source">
///     Where the list's entries come from. A <see cref="DnsFilterListSourceEnum.Subscribed" />
///     list also needs <see cref="SubscriptionUrl" />; its entries are then replaced on each
///     refresh and cannot be edited by hand (see <see cref="FilterListEntriesController" />).
/// </param>
/// <param name="SubscriptionUrl">
///     The URL a <see cref="DnsFilterListSourceEnum.Subscribed" /> list's entries are fetched
///     from. Required for a subscribed list, otherwise ignored.
/// </param>
/// <param name="RefreshInterval">
///     How often a <see cref="DnsFilterListSourceEnum.Subscribed" /> list is fetched again.
///     Ignored for a <see cref="DnsFilterListSourceEnum.Local" /> list.
/// </param>
/// <param name="IsDisabled">Whether the list is disabled.</param>
/// <param name="Comments">Free-form operator notes about the list.</param>
public record FilterListWriteRequest(
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    DnsFilterListSourceEnum Source,
    string SubscriptionUrl = null,
    TimeSpan? RefreshInterval = null,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a block or allow list without its entries, as returned by the list, get
///     and write endpoints of <c>/api/filter-lists</c>. <c>LastFetchedAtUtc</c> is when a
///     subscribed list was last fetched, and <c>OriginatingNodeId</c> the cluster node the list
///     was last written on.
/// </summary>
public record FilterListDetail(
    Guid Id,
    string Name,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action,
    DnsFilterListSourceEnum Source,
    string SubscriptionUrl,
    TimeSpan? RefreshInterval,
    DateTimeOffset? LastFetchedAtUtc,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? OriginatingNodeId = null
);

/// <summary>
///     This class carries the fields used to create or replace an entry in a
///     <see cref="DnsFilterListSourceEnum.Local" /> list (<c>POST</c> and <c>PUT</c> under
///     <c>/api/filter-lists/{listId}/entries</c>). Every write to the entries of a
///     <see cref="DnsFilterListSourceEnum.Subscribed" /> list is rejected with 409 (see
///     <see cref="FilterListEntriesController" />).
/// </summary>
/// <param name="Value">
///     The entry's value: a domain name, wildcard or regular expression (by <see cref="Kind" />)
///     in a <see cref="DnsFilterListKindEnum.Domain" /> list, or an IP address or CIDR network in
///     a <see cref="DnsFilterListKindEnum.ResponseIp" /> or
///     <see cref="DnsFilterListKindEnum.ClientSource" /> list, where <see cref="Kind" /> must be
///     <see cref="DnsFilterEntryKindEnum.Domain" /> (the default).
/// </param>
/// <param name="Comment">Free-form operator notes about the entry.</param>
/// <param name="Kind">
///     How <see cref="Value" /> is matched. Defaults to <see cref="DnsFilterEntryKindEnum.Domain" />.
///     A <see cref="DnsFilterEntryKindEnum.Wildcard" /> or <see cref="DnsFilterEntryKindEnum.Regex" />
///     value is rejected with a 400 unless it compiles and the list is a
///     <see cref="DnsFilterListKindEnum.Domain" /> list (see <see cref="FilterListEntriesController" />).
/// </param>
public record FilterListEntryWriteRequest(
    string Value,
    string Comment = null,
    DnsFilterEntryKindEnum Kind = DnsFilterEntryKindEnum.Domain
);

/// <summary>
///     This class carries one filter list entry, as returned by the list, get and write endpoints
///     under <c>/api/filter-lists/{listId}/entries</c>. <c>IsException</c> marks an exception (an adblock-syntax <c>@@</c>
///     rule from a subscribed list) that lets a name through even though another entry in the
///     same list covers it; only a subscribed list's refresh ever sets it, so it is not part of
///     <see cref="FilterListEntryWriteRequest" />.
/// </summary>
public record FilterListEntryDetail(
    Guid Id,
    Guid ListId,
    string Value,
    string Comment,
    DnsFilterEntryKindEnum Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc = null,
    Guid? OriginatingNodeId = null,
    bool IsException = false
);
