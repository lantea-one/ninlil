// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a role's own name/description/permission-claim state
///     changes -- created, updated, deleted, or granted/revoked a permission, through
///     <c>RolesController</c>. Its only real subscriber is the cluster's own identity/RBAC
///     federation, forwarding the change to every peer over the existing cluster channel, the same
///     shape <see cref="UserAccountChangedEvent" /> establishes for a user account.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the role changed.</param>
/// <param name="RoleId">The identifier of the role that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>RolesController.Delete</c>; every other mutation reports <see langword="false" />.
/// </param>
public record RoleChangedEvent(DateTimeOffset OccurredAtUtc, Guid RoleId, bool IsDeleted = false)
    : IDnsServerEvent;
