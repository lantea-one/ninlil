// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a global upstream forwarder is created, updated, deleted,
///     enabled or disabled through <c>ForwardersController</c>. Cluster sync forwards it to
///     peers; no local cache needs it, because <c>ForwarderQueryResolver</c> reads forwarders
///     from storage.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the forwarder changed.</param>
/// <param name="ForwarderId">The identifier of the forwarder that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>ForwardersController.Delete</c>).
/// </param>
public record DnsForwarderChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ForwarderId,
    bool IsDeleted = false
) : IDnsServerEvent;
