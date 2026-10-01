using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a client group
///     (<c>POST /api/client-groups</c>, <c>PUT /api/client-groups/{id}</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the group.</param>
/// <param name="IsDefault">
///     Whether this is the fallback group for a client that matches no other group's networks.
///     Setting it <see langword="true" /> takes the flag from whichever group holds it, since only
///     one group may (see <see cref="ClientGroupsController" />).
/// </param>
/// <param name="IsDisabled">Whether the group is disabled.</param>
public record ClientGroupWriteRequest(string Name, bool IsDefault = false, bool IsDisabled = false);

/// <summary>
///     This class carries a client group, as returned by the list, get and create endpoints of
///     <c>/api/client-groups</c>.
/// </summary>
/// <param name="Id">The group's unique identifier.</param>
/// <param name="Name">The group's operator-chosen, human-readable, unique name.</param>
/// <param name="IsDefault">Whether this is the fallback group for a client that matches no other group's networks.</param>
/// <param name="IsDisabled">Whether the group is currently disabled.</param>
/// <param name="CreatedAtUtc">When the group was created.</param>
/// <param name="UpdatedAtUtc">When the group was last updated, or <see langword="null" /> if never.</param>
/// <param name="PausedUntilUtc">
///     The UTC instant until which the group's pause blocks DNS resolution for every member (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity.PausedUntilUtc" />), or
///     <see langword="null" /> when no pause has been set.
/// </param>
/// <param name="IsCurrentlyPaused">
///     Whether <paramref name="PausedUntilUtc" /> is still in the future, computed when the
///     response is built, as for <see cref="ClientDetail.IsCurrentlyPaused" />.
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
///     client group (<c>POST</c> and <c>PUT</c> under <c>/api/client-groups/{groupId}/networks</c>).
///     Exactly one of <paramref name="Cidr" /> and <paramref name="ClientId" /> must be set (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupNetworkEntity" />).
/// </summary>
/// <param name="Cidr">
///     The network in CIDR notation, prefix length included (write a single address as
///     <c>/32</c> or <c>/128</c>; a bare address is rejected). <see langword="null" /> when <paramref name="ClientId" /> is set
///     instead.
/// </param>
/// <param name="ClientId">
///     A DNS-over-HTTPS ClientID label this row matches (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupNetworkEntity.ClientId" />). <see langword="null" /> when <paramref name="Cidr" /> is set instead.
/// </param>
public record ClientGroupNetworkWriteRequest(string Cidr, string ClientId = null);

/// <summary>
///     This class carries one client group network, as returned by the list, get, create and
///     replace endpoints under <c>/api/client-groups/{groupId}/networks</c>.
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
///     (<c>POST /api/client-groups/{groupId}/list-assignments</c>). There is no <c>PUT</c> or
///     <c>PATCH</c>: an assignment only joins a group to a list and has no other fields, so
///     assigning a different list means deleting one assignment and creating another.
/// </summary>
/// <param name="ListId">The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsFilterListEntity" /> to assign to this group.</param>
public record ClientGroupListAssignmentWriteRequest(Guid ListId);

/// <summary>
///     This class carries one client group list assignment, as returned by the list, get and
///     create endpoints under <c>/api/client-groups/{groupId}/list-assignments</c>.
/// </summary>
public record ClientGroupListAssignmentDetail(
    Guid Id,
    Guid ClientGroupId,
    Guid ListId,
    DateTimeOffset CreatedAtUtc
);
