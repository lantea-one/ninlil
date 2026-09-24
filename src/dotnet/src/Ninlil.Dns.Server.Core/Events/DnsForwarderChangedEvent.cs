// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a globally configured upstream forwarder changes --
///     created, updated, deleted, or enabled/disabled through <c>ForwardersController</c>. No
///     local engine subscribes to this for cache invalidation (<c>ForwarderQueryResolver</c>
///     re-reads the repository fresh on every query, per its own doc comment) -- today, its only
///     real subscriber is the cluster's own config sync.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the forwarder changed.</param>
/// <param name="ForwarderId">The identifier of the forwarder that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>ForwardersController.Delete</c>.
/// </param>
public record DnsForwarderChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ForwarderId,
    bool IsDeleted = false
) : IDnsServerEvent;
