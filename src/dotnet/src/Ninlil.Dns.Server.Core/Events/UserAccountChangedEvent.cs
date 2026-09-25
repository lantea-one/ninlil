// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a user account is created, updated or deleted, or its roles
///     or permissions change, through <c>UsersController</c>. Cluster identity sync forwards the
///     change to every peer, including the account's protected secrets decrypted by the sender.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the account changed.</param>
/// <param name="UserId">The identifier of the user account that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>UsersController.Delete</c>).
/// </param>
public record UserAccountChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid UserId,
    bool IsDeleted = false
) : IDnsServerEvent;
