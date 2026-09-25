// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a role is created, updated or deleted, or gains or loses a
///     permission, through <c>RolesController</c>. Cluster identity sync forwards it to every
///     peer, as it does <see cref="UserAccountChangedEvent" />.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the role changed.</param>
/// <param name="RoleId">The identifier of the role that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>RolesController.Delete</c>).
/// </param>
public record RoleChangedEvent(DateTimeOffset OccurredAtUtc, Guid RoleId, bool IsDeleted = false)
    : IDnsServerEvent;
