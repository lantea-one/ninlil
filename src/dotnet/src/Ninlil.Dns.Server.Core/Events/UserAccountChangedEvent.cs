// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a user account's own identity/RBAC state changes --
///     created, updated, deleted, or its role/permission-claim assignments changed, through
///     <c>UsersController</c>. Its only real subscriber is the cluster's own identity/RBAC
///     federation, forwarding the change (including a decrypt-at-sender snapshot of every
///     protected secret field) to every peer over the existing cluster channel -- the same "no
///     local engine needs this purely for cache invalidation" shape
///     <see cref="DnsFilterListChangedEvent" /> already establishes for its own config sync.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the account changed.</param>
/// <param name="UserId">The identifier of the user account that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>UsersController.Delete</c>; every other mutation (create, profile/lockout update, role
///     grant/revoke, direct permission grant/revoke) reports <see langword="false" />.
/// </param>
public record UserAccountChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid UserId,
    bool IsDeleted = false
) : IDnsServerEvent;
