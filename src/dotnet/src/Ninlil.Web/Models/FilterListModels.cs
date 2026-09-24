using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new block/allow list
///     (<c>POST /api/filter-lists</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this list.</param>
/// <param name="Kind">What kind of value this list's entries hold.</param>
/// <param name="Action">Whether a match against this list denies or explicitly permits.</param>
/// <param name="Source">
///     Where this list's own entries come from. A <see cref="DnsFilterListSourceEnum.Subscribed" />
///     list must also supply <see cref="SubscriptionUrl" /> -- see
///     <see cref="FilterListEntriesController" /> for how a subscribed list's own entries are then
///     managed.
/// </param>
/// <param name="SubscriptionUrl">
///     The URL a <see cref="DnsFilterListSourceEnum.Subscribed" /> list's entries are periodically
///     fetched from. Required when <see cref="Source" /> is <see cref="DnsFilterListSourceEnum.Subscribed" />,
///     otherwise ignored.
/// </param>
/// <param name="RefreshInterval">
///     How often a <see cref="DnsFilterListSourceEnum.Subscribed" /> list's entries are refetched.
///     Ignored for a <see cref="DnsFilterListSourceEnum.Local" /> list.
/// </param>
/// <param name="IsDisabled">Whether the list starts out disabled.</param>
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
///     This class carries a block/allow list's own metadata, for list, single-item, and
///     creation responses.
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
///     This class carries the fields used to create a new entry directly in a
///     <see cref="DnsFilterListSourceEnum.Local" /> list
///     (<c>POST /api/filter-lists/{listId}/entries</c>). See
///     <see cref="FilterListEntriesController" /> for why this, and every other write action on
///     this nested resource, is rejected for a <see cref="DnsFilterListSourceEnum.Subscribed" />
///     list.
/// </summary>
/// <param name="Value">
///     The entry's own value -- a domain name, wildcard glob, or regular expression (depending on
///     <see cref="Kind" />) for a <see cref="DnsFilterListKindEnum.Domain" /> list, or an IP
///     address/CIDR network for a <see cref="DnsFilterListKindEnum.ResponseIp" />/
///     <see cref="DnsFilterListKindEnum.ClientSource" /> list (where <see cref="Kind" /> must be
///     <see cref="DnsFilterEntryKindEnum.Domain" />, the default).
/// </param>
/// <param name="Comment">Free-form operator notes about this entry.</param>
/// <param name="Kind">
///     How <see cref="Value" /> is matched. Defaults to <see cref="DnsFilterEntryKindEnum.Domain" />.
///     A <see cref="DnsFilterEntryKindEnum.Wildcard" />/
///     <see cref="DnsFilterEntryKindEnum.Regex" /> value is rejected at write time
///     (<c>400 Bad Request</c>) unless it actually compiles, and unless the owning list is itself
///     a <see cref="DnsFilterListKindEnum.Domain" /> list -- see
///     <see cref="FilterListEntriesController" /> for the full validation.
/// </param>
public record FilterListEntryWriteRequest(
    string Value,
    string Comment = null,
    DnsFilterEntryKindEnum Kind = DnsFilterEntryKindEnum.Domain
);

/// <summary>
///     This class carries a single filter list entry's own fields, for list, single-item, and
///     creation responses.
/// </summary>
public record FilterListEntryDetail(
    Guid Id,
    Guid ListId,
    string Value,
    string Comment,
    DnsFilterEntryKindEnum Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc = null,
    Guid? OriginatingNodeId = null
);
