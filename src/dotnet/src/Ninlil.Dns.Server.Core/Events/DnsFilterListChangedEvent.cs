// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a block/allow list is created, updated, deleted, enabled or
///     disabled through <c>FilterListsController</c>. Cluster sync forwards it to peers. No
///     local cache needs it, because <c>DnsFilterEngine</c> reads the lists from storage.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the list changed.</param>
/// <param name="ListId">The identifier of the list that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>FilterListsController.Delete</c>).
/// </param>
public record DnsFilterListChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid ListId,
    bool IsDeleted = false
) : IDnsServerEvent;
