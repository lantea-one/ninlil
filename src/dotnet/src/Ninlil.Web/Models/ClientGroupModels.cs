using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a client group
///     (<c>POST</c>/<c>PUT /api/client-groups</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this group.</param>
/// <param name="IsDefault">
///     Whether this is the fallback group a client matching no other group's own networks is
///     treated as belonging to. Setting this <see langword="true" /> demotes whichever other
///     group currently holds it -- see <see cref="ClientGroupsController" /> for why exactly one
///     group may ever hold it at a time.
/// </param>
/// <param name="IsDisabled">Whether the group starts out (or is currently) disabled.</param>
public record ClientGroupWriteRequest(string Name, bool IsDefault = false, bool IsDisabled = false);

/// <summary>
///     This class carries a client group's own metadata, for list, single-item, and
///     creation responses.
/// </summary>
/// <param name="Id">This group's own unique identifier.</param>
/// <param name="Name">This group's own operator-chosen, human-readable, unique name.</param>
/// <param name="IsDefault">Whether this is the fallback group a client matching no other group's own networks is treated as belonging to.</param>
/// <param name="IsDisabled">Whether the group is currently disabled.</param>
/// <param name="CreatedAtUtc">When this row was first created.</param>
/// <param name="UpdatedAtUtc">When this row was last updated, or <see langword="null" /> if never.</param>
/// <param name="PausedUntilUtc">
///     The instant, in UTC, until which this group's instant pause blocks every member's own DNS
///     resolution -- see <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity.PausedUntilUtc" />.
///     <see langword="null" /> when this group is not currently pausing its members.
/// </param>
/// <param name="IsCurrentlyPaused">
///     Whether <paramref name="PausedUntilUtc" /> is currently in effect -- computed at read time
///     against the current instant, the same way <see cref="ClientDetail.IsCurrentlyPaused" /> is.
/// </param>
public record ClientGroupDetail(
    Guid Id,
    string Name,
    bool IsDefault,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PausedUntilUtc,
    bool IsCurrentlyPaused
);

/// <summary>
///     This class carries the fields used to create or fully replace a network belonging to a
///     client group (<c>POST</c>/<c>PUT /api/client-groups/{groupId}/networks</c>). Exactly one
///     of <paramref name="Cidr" />/<paramref name="ClientId" /> must be set -- see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupNetworkEntity" />'s own doc comment.
/// </summary>
/// <param name="Cidr">
///     The network, in CIDR notation (a bare IP address is treated as a <c>/32</c>/<c>/128</c>
///     network of one address). <see langword="null" /> when <paramref name="ClientId" /> is set
///     instead.
/// </param>
/// <param name="ClientId">
///     A DNS-over-HTTPS ClientID label this row matches (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupNetworkEntity.ClientId" />'s own doc
///     comment). <see langword="null" /> when <paramref name="Cidr" /> is set instead.
/// </param>
public record ClientGroupNetworkWriteRequest(string Cidr, string ClientId = null);

/// <summary>
///     This class carries a single client group network's own fields, for list, single-item, and
///     creation responses.
/// </summary>
public record ClientGroupNetworkDetail(
    Guid Id,
    Guid ClientGroupId,
    string Cidr,
    string ClientId,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
///     This class carries the fields used to create a new list assignment for a client group
///     (<c>POST /api/client-groups/{groupId}/list-assignments</c>). There is deliberately no
///     corresponding <c>PUT</c>/<c>PATCH</c> -- an assignment is just a join between a group and a
///     list with no mutable fields of its own; changing which list is assigned means deleting one
///     assignment and creating another.
/// </summary>
/// <param name="ListId">The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsFilterListEntity" /> to assign to this group.</param>
public record ClientGroupListAssignmentWriteRequest(Guid ListId);

/// <summary>
///     This class carries a single client group list assignment's own fields, for list,
///     single-item, and creation responses.
/// </summary>
public record ClientGroupListAssignmentDetail(
    Guid Id,
    Guid ClientGroupId,
    Guid ListId,
    DateTimeOffset CreatedAtUtc
);
